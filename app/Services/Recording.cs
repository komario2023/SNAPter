using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Snapter
{
    // =====================================================================================================
    //  GIF: a built-in encoder (palette by median cut, optional dithering, only the changed part of each frame)
    // =====================================================================================================
    internal static class GifEncoder
    {
        /// <summary>Writes an animated GIF. delaysMs: how long each frame is shown. colors: 4 to 256. loop: 0 = for ever.</summary>
        public static void Save(string path, IList<Bitmap> frames, IList<int> delaysMs, int colors, bool dither, int loop, bool mergeIdentical)
        {
            if (frames.Count == 0) throw new ArgumentException("no frames");
            int w = frames[0].Width, h = frames[0].Height;
            int[][] px = new int[frames.Count][];
            for (int i = 0; i < frames.Count; i++) px[i] = ToRgb(frames[i], w, h);

            byte[] pal = MakePalette(px, Math.Max(4, Math.Min(256, colors)));
            int n = pal.Length / 3;
            byte[] lut = BuildLut(pal, n);

            // palette indices of every frame
            byte[][] idx = new byte[frames.Count][];
            for (int i = 0; i < frames.Count; i++) idx[i] = Quantize(px[i], w, h, pal, lut, dither);

            // what is written for each frame: the changed rectangle only, with the delay (identical frames join the previous one)
            List<FrameOut> outs = new List<FrameOut>();
            for (int i = 0; i < frames.Count; i++)
            {
                int delay = i < delaysMs.Count ? delaysMs[i] : 100;
                if (i == 0) { outs.Add(new FrameOut { X = 0, Y = 0, W = w, H = h, Data = idx[0], DelayMs = delay }); continue; }
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                byte[] a = idx[i - 1], b = idx[i];
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                        if (a[row + x] != b[row + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                }
                if (x1 < 0)
                {
                    if (mergeIdentical) { outs[outs.Count - 1].DelayMs += delay; continue; }
                    outs.Add(new FrameOut { X = 0, Y = 0, W = 1, H = 1, Data = new byte[] { b[0] }, DelayMs = delay });
                    continue;
                }
                int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
                byte[] part = new byte[cw * ch];
                for (int y = 0; y < ch; y++) Buffer.BlockCopy(b, (y0 + y) * w + x0, part, y * cw, cw);
                outs.Add(new FrameOut { X = x0, Y = y0, W = cw, H = ch, Data = part, DelayMs = delay });
            }

            int bits = 1;
            while ((1 << bits) < n) bits++;
            bits = Math.Max(2, bits);
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("GIF89a"));
                bw.Write((ushort)w); bw.Write((ushort)h);
                bw.Write((byte)(0x80 | ((bits - 1) << 4) | (bits - 1)));   // a global colour table, its size
                bw.Write((byte)0); bw.Write((byte)0);
                int tableSize = 1 << bits;
                for (int i = 0; i < tableSize; i++)
                {
                    if (i < n) { bw.Write(pal[i * 3]); bw.Write(pal[i * 3 + 1]); bw.Write(pal[i * 3 + 2]); }
                    else { bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); }
                }
                // looping: Netscape extension
                bw.Write((byte)0x21); bw.Write((byte)0xFF); bw.Write((byte)11);
                bw.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                bw.Write((byte)3); bw.Write((byte)1); bw.Write((ushort)Math.Max(0, Math.Min(65535, loop))); bw.Write((byte)0);

                foreach (FrameOut f in outs)
                {
                    bw.Write((byte)0x21); bw.Write((byte)0xF9); bw.Write((byte)4);
                    bw.Write((byte)0x04);                                      // disposal 1: leave the picture, the next frame paints over it
                    bw.Write((ushort)Math.Max(2, (int)Math.Round(f.DelayMs / 10.0)));
                    bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((byte)0x2C);
                    bw.Write((ushort)f.X); bw.Write((ushort)f.Y); bw.Write((ushort)f.W); bw.Write((ushort)f.H);
                    bw.Write((byte)0);                                         // no local table
                    bw.Write((byte)bits);                                      // the smallest LZW code size
                    LzwWrite(bw, f.Data, bits);
                }
                bw.Write((byte)0x3B);
            }
        }

        private sealed class FrameOut { public int X, Y, W, H; public byte[] Data; public int DelayMs; }

        private static int[] ToRgb(Bitmap bmp, int w, int h)
        {
            int[] arr = new int[w * h];
            using (Bitmap fit = (bmp.Width == w && bmp.Height == h) ? null : new Bitmap(bmp, w, h))
            {
                Bitmap src = fit ?? bmp;
                BitmapData d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (d.Stride == w * 4) Marshal.Copy(d.Scan0, arr, 0, w * h);
                    else for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), arr, y * w, w);
                }
                finally { src.UnlockBits(d); }
            }
            for (int i = 0; i < arr.Length; i++) arr[i] &= 0xFFFFFF;
            return arr;
        }

        // ---- the palette: median cut over a sample of the pixels of all frames ----
        private static byte[] MakePalette(int[][] frames, int colors)
        {
            List<int> sample = new List<int>();
            long total = 0; foreach (int[] f in frames) total += f.Length;
            long step = Math.Max(1, total / 90000);
            long k = 0;
            foreach (int[] f in frames)
                for (int i = 0; i < f.Length; i++, k++)
                    if (k % step == 0) sample.Add(f[i]);
            List<int[]> boxes = new List<int[]> { sample.ToArray() };
            while (boxes.Count < colors)
            {
                // split the box with the widest range of one channel
                int best = -1; int bestRange = 0; int bestCh = 0;
                for (int bi = 0; bi < boxes.Count; bi++)
                {
                    int[] box = boxes[bi];
                    if (box.Length < 2) continue;
                    int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
                    foreach (int c in box)
                    {
                        int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                        if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                        if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                        if (b < bMin) bMin = b; if (b > bMax) bMax = b;
                    }
                    int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
                    int range = Math.Max(rr, Math.Max(gr, br));
                    if (range > bestRange) { bestRange = range; best = bi; bestCh = (range == rr) ? 16 : (range == gr) ? 8 : 0; }
                }
                if (best < 0 || bestRange == 0) break;
                int[] src = boxes[best];
                int sh = bestCh;
                Array.Sort(src, (x, y) => ((x >> sh) & 255).CompareTo((y >> sh) & 255));
                int mid = src.Length / 2;
                int[] left = new int[mid], right = new int[src.Length - mid];
                Array.Copy(src, 0, left, 0, mid); Array.Copy(src, mid, right, 0, right.Length);
                boxes[best] = left; boxes.Add(right);
            }
            List<byte> pal = new List<byte>();
            foreach (int[] box in boxes)
            {
                if (box.Length == 0) continue;
                long sr = 0, sg = 0, sb = 0;
                foreach (int c in box) { sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; }
                pal.Add((byte)(sr / box.Length)); pal.Add((byte)(sg / box.Length)); pal.Add((byte)(sb / box.Length));
            }
            if (pal.Count < 6) { pal.AddRange(new byte[] { 0, 0, 0, 255, 255, 255 }); }
            return pal.ToArray();
        }

        // a table from a 15 bit colour to the nearest palette entry
        private static byte[] BuildLut(byte[] pal, int n)
        {
            byte[] lut = new byte[32768];
            for (int c = 0; c < 32768; c++)
            {
                int r = ((c >> 10) & 31) * 255 / 31, g = ((c >> 5) & 31) * 255 / 31, b = (c & 31) * 255 / 31;
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    int dr = r - pal[i * 3], dg = g - pal[i * 3 + 1], db = b - pal[i * 3 + 2];
                    int d = dr * dr * 2 + dg * dg * 4 + db * db * 3;   // the eye is most sensitive to green
                    if (d < bestD) { bestD = d; best = i; if (d == 0) break; }
                }
                lut[c] = (byte)best;
            }
            return lut;
        }

        private static byte[] Quantize(int[] px, int w, int h, byte[] pal, byte[] lut, bool dither)
        {
            byte[] res = new byte[w * h];
            if (!dither)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    int c = px[i];
                    res[i] = lut[(((c >> 19) & 31) << 10) | (((c >> 11) & 31) << 5) | ((c >> 3) & 31)];
                }
                return res;
            }
            // Floyd-Steinberg
            int[] er = new int[w + 2], eg = new int[w + 2], eb = new int[w + 2];
            int[] nr = new int[w + 2], ng = new int[w + 2], nb = new int[w + 2];
            for (int y = 0; y < h; y++)
            {
                Array.Clear(nr, 0, nr.Length); Array.Clear(ng, 0, ng.Length); Array.Clear(nb, 0, nb.Length);
                for (int x = 0; x < w; x++)
                {
                    int c = px[y * w + x];
                    int r = Clamp(((c >> 16) & 255) + er[x + 1] / 16), g = Clamp(((c >> 8) & 255) + eg[x + 1] / 16), b = Clamp((c & 255) + eb[x + 1] / 16);
                    int ix = lut[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)];
                    res[y * w + x] = (byte)ix;
                    int dr = r - pal[ix * 3], dg = g - pal[ix * 3 + 1], db = b - pal[ix * 3 + 2];
                    er[x + 2] += dr * 7; eg[x + 2] += dg * 7; eb[x + 2] += db * 7;
                    nr[x] += dr * 3; ng[x] += dg * 3; nb[x] += db * 3;
                    nr[x + 1] += dr * 5; ng[x + 1] += dg * 5; nb[x + 1] += db * 5;
                    nr[x + 2] += dr; ng[x + 2] += dg; nb[x + 2] += db;
                }
                int[] t;
                t = er; er = nr; nr = t; t = eg; eg = ng; ng = t; t = eb; eb = nb; nb = t;
            }
            return res;
        }

        private static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        // ---- LZW (the GIF flavour) written in sub-blocks of up to 255 bytes ----
        private sealed class BitSink
        {
            private readonly BinaryWriter _w;
            private readonly byte[] _block = new byte[255];
            private int _blockLen, _bitBuf, _bitCnt;
            public BitSink(BinaryWriter w) { _w = w; }

            public void Put(int code, int size)
            {
                _bitBuf |= code << _bitCnt;
                _bitCnt += size;
                while (_bitCnt >= 8) { AddByte((byte)(_bitBuf & 255)); _bitBuf >>= 8; _bitCnt -= 8; }
            }

            private void AddByte(byte b)
            {
                _block[_blockLen++] = b;
                if (_blockLen == 255) FlushBlock();
            }

            private void FlushBlock()
            {
                if (_blockLen == 0) return;
                _w.Write((byte)_blockLen); _w.Write(_block, 0, _blockLen);
                _blockLen = 0;
            }

            public void Finish()
            {
                if (_bitCnt > 0) AddByte((byte)(_bitBuf & 255));
                _bitBuf = 0; _bitCnt = 0;
                FlushBlock();
                _w.Write((byte)0);
            }
        }

        private static void LzwWrite(BinaryWriter bw, byte[] data, int minCode)
        {
            BitSink sink = new BitSink(bw);
            int clear = 1 << minCode, eoi = clear + 1;
            int next = eoi + 1, size = minCode + 1, maxCode = (1 << size) - 1;
            Dictionary<int, int> dict = new Dictionary<int, int>();

            Action<int> emit = delegate (int code)
            {
                sink.Put(code, size);
                if (next > maxCode && size < 12) { size++; maxCode = (1 << size) - 1; }
            };
            Action reset = delegate { dict.Clear(); next = eoi + 1; size = minCode + 1; maxCode = (1 << size) - 1; };

            emit(clear);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i];
                int key = (prefix << 8) | k;
                int code;
                if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix);
                if (next < 4096) dict[key] = next++;
                else { emit(clear); reset(); }
                prefix = k;
            }
            emit(prefix);
            emit(eoi);
            sink.Finish();
        }
    }

    // =====================================================================================================
    //  GIF recording: frames are taken by a background thread, the file is made when the recording stops
    // =====================================================================================================
    internal sealed class GifRecorder
    {
        private readonly Rectangle _region;
        private readonly Settings _s;
        private readonly List<Bitmap> _frames = new List<Bitmap>();
        private readonly List<long> _times = new List<long>();
        private Thread _thread;
        private volatile bool _stop;
        private long _bytes;
        private readonly Stopwatch _sw = new Stopwatch();

        public bool HitLimit { get; private set; }
        public bool Running { get { return _thread != null && _thread.IsAlive; } }
        public int FrameCount { get { lock (_frames) return _frames.Count; } }
        public double Seconds { get { return _sw.Elapsed.TotalSeconds; } }
        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }

        public GifRecorder(Rectangle region, Settings s) { _region = region; _s = s; }

        public void Start()
        {
            int scale = Math.Max(10, Math.Min(100, _s.GifScale));
            FrameWidth = Math.Max(8, _region.Width * scale / 100);
            FrameHeight = Math.Max(8, _region.Height * scale / 100);
            _sw.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "GifRecorder" };
            _thread.Start();
        }

        private void Loop()
        {
            double interval = 1000.0 / Math.Max(1, _s.GifFps);
            double next = 0;
            long limitBytes = 700L * 1024 * 1024;
            while (!_stop)
            {
                long now = _sw.ElapsedMilliseconds;
                if (now >= _s.GifMaxSeconds * 1000L) { HitLimit = true; break; }
                if (now >= next)
                {
                    Bitmap shot = null;
                    try
                    {
                        shot = ScreenCapture.Capture(_region, _s.GifCursor);
                        if (shot.Width != FrameWidth || shot.Height != FrameHeight)
                        {
                            Bitmap small = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format32bppArgb);
                            using (Graphics g = Graphics.FromImage(small))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(shot, 0, 0, FrameWidth, FrameHeight);
                            }
                            shot.Dispose(); shot = small;
                        }
                    }
                    catch (Exception ex) { Log.Write("gif capture error: " + ex.Message); }
                    if (shot != null)
                    {
                        lock (_frames) { _frames.Add(shot); _times.Add(now); }
                        _bytes += (long)FrameWidth * FrameHeight * 4;
                        if (_bytes > limitBytes) { HitLimit = true; break; }
                    }
                    next += interval;
                    if (next < now - interval) next = now;   // the computer is slower than the wanted rate: do not try to catch up
                }
                else Thread.Sleep((int)Math.Max(1, Math.Min(10, next - now)));
            }
            _sw.Stop();
        }

        /// <summary>Ends the recording (waits for the capturing thread).</summary>
        public void Stop()
        {
            _stop = true;
            if (_thread != null) _thread.Join(5000);
        }

        /// <summary>Makes the GIF file from what was recorded. Call after Stop.</summary>
        public int Finish(string path)
        {
            List<Bitmap> frames; List<long> times;
            lock (_frames) { frames = new List<Bitmap>(_frames); times = new List<long>(_times); }
            if (frames.Count == 0) throw new InvalidOperationException("nothing was recorded");
            List<int> delays = new List<int>();
            for (int i = 0; i < frames.Count; i++)
                delays.Add(i + 1 < frames.Count ? (int)(times[i + 1] - times[i]) : (int)(1000.0 / Math.Max(1, _s.GifFps)));
            try { GifEncoder.Save(path, frames, delays, _s.GifColors, _s.GifDither, _s.GifLoop, _s.GifSkipDuplicates); }
            finally { foreach (Bitmap b in frames) b.Dispose(); lock (_frames) { _frames.Clear(); } }
            return frames.Count;
        }
    }

    // =====================================================================================================
    //  Video: FFmpeg records the screen itself (gdigrab) and encodes it while it records
    // =====================================================================================================
    internal sealed class FfmpegRecorder
    {
        private Process _p;
        private readonly StringBuilder _log = new StringBuilder();
        public string OutputPath { get; private set; }
        public string LastLog { get { lock (_log) return _log.ToString(); } }
        public bool Running { get { return _p != null && !_p.HasExited; } }

        /// <summary>ffmpeg.exe: the path from the settings, else the PATH, else the usual folders. Null when it is not found.</summary>
        public static string Find(Settings s)
        {
            if (!string.IsNullOrWhiteSpace(s.FfmpegPath))
            {
                string p = Environment.ExpandEnvironmentVariables(s.FfmpegPath.Trim().Trim('"'));
                if (Directory.Exists(p)) p = System.IO.Path.Combine(p, "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string c = System.IO.Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                    if (File.Exists(c)) return c;
                }
                catch { }
            }
            string[] usual =
            {
                @"C:\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                @"D:\ffmpeg\bin\ffmpeg.exe", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe"),
                Environment.ExpandEnvironmentVariables(@"%ProgramData%\chocolatey\bin\ffmpeg.exe"), Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\scoop\shims\ffmpeg.exe")
            };
            foreach (string c in usual) if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>The command line for recording the region to a file (the settings: frame rate, codec, quality or bitrate, scale, audio, extra).</summary>
        public static string BuildArgs(Settings s, Rectangle region, string output)
        {
            int w = Math.Max(16, region.Width & ~1), h = Math.Max(16, region.Height & ~1);   // H.264 wants even sizes
            StringBuilder a = new StringBuilder();
            a.Append("-hide_banner -y ");
            a.Append("-f gdigrab -framerate ").Append(Math.Max(1, s.VideoFps));
            a.Append(" -draw_mouse ").Append(s.VideoCursor ? 1 : 0);
            a.Append(" -offset_x ").Append(region.X).Append(" -offset_y ").Append(region.Y);
            a.Append(" -video_size ").Append(w).Append('x').Append(h);
            a.Append(" -i desktop ");
            bool audio = string.Equals(s.VideoAudio, "device", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.VideoAudioDevice);
            if (audio) a.Append("-f dshow -i audio=\"").Append(s.VideoAudioDevice.Replace("\"", "")).Append("\" ");
            string codec = string.IsNullOrWhiteSpace(s.VideoCodec) ? "libx264" : s.VideoCodec.Trim();
            a.Append("-c:v ").Append(codec).Append(' ');
            if (!string.IsNullOrWhiteSpace(s.VideoPreset) && (codec.StartsWith("libx") || codec.Contains("nvenc")))
                a.Append("-preset ").Append(s.VideoPreset.Trim()).Append(' ');
            if (string.Equals(s.VideoRateMode, "bitrate", StringComparison.OrdinalIgnoreCase))
            {
                a.Append("-b:v ").Append(Math.Max(100, s.VideoBitrateKbps)).Append("k ");
                if (s.VideoMaxrateKbps > 0) a.Append("-maxrate ").Append(s.VideoMaxrateKbps).Append("k -bufsize ").Append(s.VideoMaxrateKbps * 2).Append("k ");
            }
            else if (codec.StartsWith("libx")) a.Append("-crf ").Append(Math.Max(0, Math.Min(51, s.VideoCrf))).Append(' ');
            else if (codec.Contains("nvenc")) a.Append("-rc vbr -cq ").Append(Math.Max(0, Math.Min(51, s.VideoCrf))).Append(" -b:v 0 ");
            else a.Append("-b:v ").Append(Math.Max(100, s.VideoBitrateKbps)).Append("k ");
            if (!string.IsNullOrWhiteSpace(s.VideoPixFmt)) a.Append("-pix_fmt ").Append(s.VideoPixFmt.Trim()).Append(' ');
            if (s.VideoScale < 100) a.Append("-vf \"scale=trunc(iw*").Append(s.VideoScale).Append("/200)*2:trunc(ih*").Append(s.VideoScale).Append("/200)*2\" ");
            if (audio) a.Append("-c:a aac -b:a 160k ");
            if (s.VideoMaxSeconds > 0) a.Append("-t ").Append(s.VideoMaxSeconds).Append(' ');
            if (!string.IsNullOrWhiteSpace(s.VideoExtra)) a.Append(s.VideoExtra.Trim()).Append(' ');
            a.Append("-movflags +faststart \"").Append(output).Append('"');
            return a.ToString();
        }

        public bool Start(Settings s, Rectangle region, string output, out string error)
        {
            error = null;
            string exe = Find(s);
            if (exe == null) { error = "ffmpeg.exe was not found (Settings - Recording: the path to ffmpeg)"; return false; }
            OutputPath = output;
            ProcessStartInfo psi = new ProcessStartInfo(exe, BuildArgs(s, region, output))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true
            };
            try
            {
                _p = Process.Start(psi);
                _p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e) { if (e.Data != null) lock (_log) { _log.AppendLine(e.Data); if (_log.Length > 20000) _log.Remove(0, 10000); } };
                _p.BeginErrorReadLine();
                _p.OutputDataReceived += delegate { };
                _p.BeginOutputReadLine();
                Thread.Sleep(600);
                if (_p.HasExited) { error = "ffmpeg stopped at once: " + Tail(); return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private string Tail()
        {
            string t = LastLog.Trim();
            string[] lines = t.Split('\n');
            return string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - 3)).Select(l => l.Trim()));
        }

        /// <summary>Ends the recording nicely (the letter q), so that the file is finished correctly; kills ffmpeg if it does not answer.</summary>
        public bool Stop(out string error)
        {
            error = null;
            if (_p == null) return false;
            try
            {
                if (!_p.HasExited)
                {
                    try { _p.StandardInput.WriteLine("q"); _p.StandardInput.Flush(); } catch { }
                    if (!_p.WaitForExit(10000)) { try { _p.Kill(); } catch { } }
                }
                _p.WaitForExit(2000);
            }
            catch (Exception ex) { error = ex.Message; }
            bool ok = File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 1000;
            if (!ok && error == null) error = Tail();
            return ok;
        }
    }

    // =====================================================================================================
    //  Scrolling screenshot: scroll with the wheel, take the pieces, join them where they overlap
    // =====================================================================================================
    internal static class ScrollCapture
    {
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

        /// <summary>The signature of every row of a picture (a hash of its pixels).</summary>
        public static long[] RowHashes(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            long[] res = new long[h];
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), row, 0, w);
                    long hash = 1469598103934665603L;
                    for (int x = 0; x < w; x++) { hash ^= (row[x] & 0xFFFFFF); hash *= 1099511628211L; }
                    res[y] = hash;
                }
            }
            finally { bmp.UnlockBits(d); }
            return res;
        }

        /// <summary>How far the content moved up between two pictures taken one after the other: the rows of "next" that
        /// are the rows of "prev" shifted by the result. 0: the pictures are the same (the page did not move). -1: no match found.</summary>
        public static int FindShift(long[] prev, long[] next, int skipTop, int skipBottom, int tolerancePct)
        {
            int h = Math.Min(prev.Length, next.Length);
            int top = Math.Min(skipTop, h / 3), bottom = Math.Min(skipBottom, h / 3);
            // the same: nothing scrolled
            int diff0 = 0, rows0 = 0;
            for (int y = top; y < h - bottom; y++) { rows0++; if (prev[y] != next[y]) diff0++; }
            if (rows0 > 0 && diff0 * 100 <= tolerancePct * rows0 / 2) return 0;

            // a row that differs from the row above it carries information (a blank page matches every shift, which says nothing)
            bool[] info = new bool[h];
            for (int y = 1; y < h; y++) info[y] = next[y] != next[y - 1];

            int bestShift = -1; double bestScore = double.MaxValue; int bestInfo = 0;
            for (int s = 1; s < h - top - bottom; s++)
            {
                int y0 = top, y1 = Math.Min(h - s, h - bottom);
                int rows = y1 - y0;
                if (rows < Math.Max(8, h / 12)) break;
                int mism = 0, inf = 0;
                for (int y = y0; y < y1; y++)
                {
                    if (prev[y + s] != next[y]) mism++;
                    else if (info[y]) inf++;
                }
                if (mism * 100 > tolerancePct * rows) continue;
                if (inf < Math.Max(6, rows / 40)) continue;
                double score = (double)mism / rows;
                if (score < bestScore - 1e-9 || (Math.Abs(score - bestScore) < 1e-9 && inf > bestInfo)) { bestScore = score; bestShift = s; bestInfo = inf; }
            }
            return bestShift;
        }

        /// <summary>Joins pictures taken while scrolling. shifts[i] = how far picture i+1 moved relative to picture i.
        /// skipTop / skipBottom: a fixed header and footer that stay on every picture (taken once).</summary>
        public static Bitmap Stitch(IList<Bitmap> frames, IList<int> shifts, int skipTop, int skipBottom)
        {
            int w = frames[0].Width, h = frames[0].Height;
            int footer = Math.Min(skipBottom, h / 3);
            List<KeyValuePair<Bitmap, int[]>> parts = new List<KeyValuePair<Bitmap, int[]>>();
            parts.Add(new KeyValuePair<Bitmap, int[]>(frames[0], new int[] { 0, h - footer }));
            int total = h - footer;
            for (int i = 1; i < frames.Count; i++)
            {
                int s = shifts[i - 1];
                if (s <= 0) continue;
                int y1 = h - footer, y0 = Math.Max(0, y1 - s);
                parts.Add(new KeyValuePair<Bitmap, int[]>(frames[i], new int[] { y0, y1 }));
                total += y1 - y0;
            }
            if (footer > 0) { parts.Add(new KeyValuePair<Bitmap, int[]>(frames[frames.Count - 1], new int[] { h - footer, h })); total += footer; }
            Bitmap res = new Bitmap(w, total, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(res))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                int y = 0;
                foreach (KeyValuePair<Bitmap, int[]> p in parts)
                {
                    int a = p.Value[0], b = p.Value[1];
                    g.DrawImage(p.Key, new Rectangle(0, y, w, b - a), new Rectangle(0, a, w, b - a), GraphicsUnit.Pixel);
                    y += b - a;
                }
            }
            return res;
        }

        public sealed class Result { public Bitmap Image; public int Frames; public string Why; }

        /// <summary>Scrolls the area under the mouse with the wheel and joins the pictures. Esc ends it early (what is taken so far is kept).</summary>
        public static Result Run(Rectangle region, Settings s, Action<string> status)
        {
            List<Bitmap> frames = new List<Bitmap>();
            List<int> shifts = new List<int>();
            string why = "end of the page";
            try
            {
                Thread.Sleep(Math.Max(0, s.ScrollStartDelay));
                SetCursorPos(region.X + region.Width / 2, region.Y + region.Height / 2);
                Thread.Sleep(150);
                frames.Add(ScreenCapture.Capture(region, false));
                long[] prev = RowHashes(frames[0]);
                int height = region.Height;
                while (frames.Count < s.ScrollMaxFrames && height < s.ScrollMaxHeight)
                {
                    if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { why = "stopped with Esc"; break; }
                    Bitmap f = null; long[] cur = null; int shift = 0;
                    // the wheel goes to the window under the mouse: the pointer is put back in the area before every step.
                    // "nothing moved" must be seen twice (a slow page, a pointer that was moved away) before it counts as the end
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        SetCursorPos(region.X + region.Width / 2, region.Y + region.Height / 2);
                        mouse_event(0x0800, 0, 0, -120 * Math.Max(1, s.ScrollNotches), UIntPtr.Zero);   // MOUSEEVENTF_WHEEL, down
                        Thread.Sleep(Math.Max(50, s.ScrollDelayMs) * (attempt + 1));
                        if (f != null) f.Dispose();
                        f = ScreenCapture.Capture(region, false);
                        cur = RowHashes(f);
                        shift = FindShift(prev, cur, s.ScrollSkipTop, s.ScrollSkipBottom, s.ScrollTolerance);
                        if (shift != 0) break;
                    }
                    if (shift == 0) { f.Dispose(); why = "end of the page"; break; }
                    if (shift < 0) { f.Dispose(); why = "the pieces could not be joined (the page changes while scrolling)"; break; }
                    frames.Add(f); shifts.Add(shift); prev = cur;
                    height += shift;
                    if (status != null) status(frames.Count + " / " + height + " px");
                }
                if (frames.Count >= s.ScrollMaxFrames) why = "the limit of pieces";
                else if (height >= s.ScrollMaxHeight) why = "the limit of height";
                Bitmap img = Stitch(frames, shifts, s.ScrollSkipTop, s.ScrollSkipBottom);
                return new Result { Image = img, Frames = frames.Count, Why = why };
            }
            finally { foreach (Bitmap b in frames) b.Dispose(); }
        }
    }
}
