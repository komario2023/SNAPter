using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Snapter
{
    /// <summary>Clipboard (PNG + bitmap/DIB, so Telegram, Word, Paint and browsers all paste it) and files.</summary>
    internal static class ImageOutput
    {
        public static void CopyToClipboard(Bitmap bmp)
        {
            MemoryStream png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);

            DataObject data = new DataObject();
            data.SetData("PNG", false, png);
            data.SetData(DataFormats.Bitmap, true, new Bitmap(bmp));

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    if (attempt >= 5) throw;
                    Thread.Sleep(80);
                }
            }
        }

        public static readonly string[] Formats = { "png", "jpg", "webp", "bmp" };

        /// <summary>"jpeg" and "JPG" become "jpg"; an unknown name becomes "png".</summary>
        public static string NormalizeFormat(string f)
        {
            f = (f ?? "").Trim().ToLowerInvariant().TrimStart('.');
            if (f == "jpeg") f = "jpg";
            return Array.IndexOf(Formats, f) >= 0 ? f : "png";
        }

        /// <summary>The format of a file save: the general one, or the separate one for "whole screen to file".</summary>
        public static string FormatFor(Settings s, bool fullscreen)
        {
            if (fullscreen && !string.IsNullOrEmpty(s.FullscreenFormat) && !string.Equals(s.FullscreenFormat, "same", StringComparison.OrdinalIgnoreCase))
                return NormalizeFormat(s.FullscreenFormat);
            return NormalizeFormat(s.Format);
        }

        /// <summary>Saves the image to the folder and name template from settings, returns the full path.</summary>
        public static string Save(Bitmap bmp, Settings settings, bool fullscreen = false)
        {
            string folder = Environment.ExpandEnvironmentVariables(settings.SaveFolder);
            if (!string.IsNullOrWhiteSpace(settings.SubfolderTemplate))
            {
                // sub-folders by date or by program (output.subfolder_template, for example {yyyy-MM} or {process})
                string sub = ExpandTemplate(settings.SubfolderTemplate, DateTime.Now);
                foreach (char c in Path.GetInvalidFileNameChars()) sub = sub.Replace(c, '_');
                sub = sub.Trim().Trim('.');
                if (sub.Length > 0) folder = Path.Combine(folder, sub);
            }
            Directory.CreateDirectory(folder);

            string name = ExpandTemplate(settings.FileNameTemplate, DateTime.Now);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

            string ext = "." + FormatFor(settings, fullscreen);

            string path = UniquePath(folder, name, ext);

            SaveTo(bmp, path, settings);
            return path;
        }

        /// <summary>Writes the image to a chosen path; the format follows the extension (.png .jpg .webp .bmp),
        /// quality comes from the settings.</summary>
        public static void SaveTo(Bitmap bmp, string path, Settings settings)
        {
            switch (NormalizeFormat(Path.GetExtension(path)))
            {
                case "jpg":
                    using (Bitmap flat = OnWhite(bmp)) SaveJpeg(flat ?? bmp, path, settings.JpgQuality);
                    break;
                case "webp": SaveWebp(bmp, path, settings.WebpQuality, settings.WebpLossless); break;
                case "bmp":
                    using (Bitmap flat2 = OnWhite(bmp)) (flat2 ?? bmp).Save(path, ImageFormat.Bmp);
                    break;
                default: bmp.Save(path, ImageFormat.Png); break;
            }
        }

        /// <summary>JPG and BMP have no transparency (rounded corners of a rounded region): such a picture is put on white.
        /// Returns null when the picture has no alpha channel and can be saved as it is.</summary>
        private static Bitmap OnWhite(Bitmap src)
        {
            if (!Image.IsAlphaPixelFormat(src.PixelFormat)) return null;
            Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return dst;
        }

        /// <summary>The picture as file bytes in the given format (png, jpg, webp, bmp), with the quality from the settings.</summary>
        public static byte[] Encode(Bitmap bmp, Settings settings, string format, out string ext)
        {
            ext = NormalizeFormat(format);
            string tmp = Path.Combine(Path.GetTempPath(), "snapter_" + Guid.NewGuid().ToString("N") + "." + ext);
            try
            {
                SaveTo(bmp, tmp, settings);
                return File.ReadAllBytes(tmp);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static void SaveJpeg(Bitmap bmp, string path, int quality)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) codec = c;
            if (codec == null) { bmp.Save(path, ImageFormat.Jpeg); return; }
            using (EncoderParameters ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(10, Math.Min(100, quality)));
                bmp.Save(path, codec, ep);
            }
        }

        /// <summary>WebP through SkiaSharp (System.Drawing cannot write it). Lossless ignores the quality.</summary>
        private static void SaveWebp(Bitmap bmp, string path, int quality, bool lossless)
        {
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                SkiaSharp.SKImageInfo info = new SkiaSharp.SKImageInfo(bmp.Width, bmp.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
                using (SkiaSharp.SKBitmap sk = new SkiaSharp.SKBitmap())
                {
                    if (!sk.InstallPixels(info, d.Scan0, d.Stride)) throw new InvalidOperationException("WebP: cannot read the pixels");
                    using (SkiaSharp.SKPixmap pm = sk.PeekPixels())
                    using (SkiaSharp.SKData data = pm.Encode(new SkiaSharp.SKWebpEncoderOptions(
                        lossless ? SkiaSharp.SKWebpEncoderCompression.Lossless : SkiaSharp.SKWebpEncoderCompression.Lossy,
                        Math.Max(1, Math.Min(100, quality)))))
                    {
                        if (data == null) throw new InvalidOperationException("WebP: the encoder returned nothing");
                        using (FileStream fs = new FileStream(path, FileMode.Create))
                            data.SaveTo(fs);
                    }
                }
            }
            finally { bmp.UnlockBits(d); }
        }

        /// <summary>Title and program of the window that was active when the capture started (tokens {window} and {process}).</summary>
        public static string WindowTitle = "";
        public static string ProcessName = "";

        /// <summary>The path folder + name + ext; when such a file exists: "name 2", "name 3", "name 4"... (a space and a number, the first free one).
        /// A file is never overwritten by a save that did not ask for it.</summary>
        public static string UniquePath(string folder, string name, string ext)
        {
            string path = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, name + " " + n + ext);
            return path;
        }

        /// <summary>The same for a name without the folder part, for a "Save as" dialog: the first name that is free in the folder.</summary>
        public static string UniqueName(string folder, string name, string ext)
        {
            return Path.GetFileNameWithoutExtension(UniquePath(folder, name, ext));
        }

        /// <summary>Ready file name templates for the list in Settings - Saving, from the shortest to the fullest: time, date, short date, date + time,
        /// short date + time, then the same with seconds (a colon is not allowed in a Windows file name, so the time uses a hyphen).</summary>
        public static readonly string[] NamePresets =
        {
            "{HH-mm}", "{dd.MM}", "{dd.MM.yy}", "{dd.MM} {HH-mm}", "{dd.MM.yy} {HH-mm}",
            "{HH-mm-ss}", "{dd.MM} {HH-mm-ss}", "{dd.MM.yy} {HH-mm-ss}"
        };

        public static string ExpandTemplate(string template, DateTime now)
        {
            return Regex.Replace(template, @"\{([^{}]+)\}", delegate (Match m)
            {
                string key = m.Groups[1].Value;
                if (key == "window") return string.IsNullOrWhiteSpace(WindowTitle) ? "window" : WindowTitle;
                if (key == "process") return string.IsNullOrWhiteSpace(ProcessName) ? "app" : ProcessName;
                try { return now.ToString(key); }
                catch { return m.Value; }
            });
        }
    }
}
