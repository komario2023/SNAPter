using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class SettingsWindow
    {
        private void BuildMorePages()
        {
            // ---- extra rows on older pages ----
            StackPanel cap = PageByTitle(_i.T("settings.tab.capture"));
            CheckBox incCursor = Check(_i.T("settings.include_cursor"), _s.IncludeCursor);
            cap.Children.Add(incCursor);
            TextBox fixedSize = Input(_s.FixedSize, 120);
            cap.Children.Add(Row(_i.T("settings.fixed_size"), fixedSize));
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "capture");
                o["include_cursor"] = incCursor.IsChecked == true;
                o["fixed_size"] = fixedSize.Text.Trim();
            });

            StackPanel sav = PageByTitle(_i.T("settings.tab.output"));
            TextBox subTpl = Input(_s.SubfolderTemplate, 220);
            sav.Children.Add(Row(_i.T("settings.subfolder"), subTpl));
            sav.Children.Add(Hint(_i.T("settings.tokens_hint")));
            _savers.Add(r => { Settings.Section(r, "output")["subfolder_template"] = subTpl.Text.Trim(); });

            StackPanel gen = PageByTitle(_i.T("settings.tab.general"));
            CheckBox remTool = Check(_i.T("settings.remember_tool"), _s.RememberTool);
            CheckBox remFolder = Check(_i.T("settings.remember_folder"), _s.RememberFolder);
            gen.Children.Insert(gen.Children.Count - 1, remTool);
            gen.Children.Insert(gen.Children.Count - 1, remFolder);
            CheckBox remTab = Check(_i.T("settings.remember_tab"), _s.RememberSettingsTab);
            gen.Children.Insert(gen.Children.Count - 1, remTab);
            CheckBox autoRebuild = Check(_i.T("settings.auto_rebuild"), _s.AutoRebuild);
            Tip(autoRebuild, "settings.auto_rebuild.tip");
            gen.Children.Insert(gen.Children.Count - 1, autoRebuild);
            _savers.Add(r =>
            {
                Settings.Section(r, "startup")["auto_rebuild"] = autoRebuild.IsChecked == true;
                Settings.Section(r, "startup")["remember_settings_tab"] = remTab.IsChecked == true;
                Settings.Section(r, "startup")["remember_last_tool"] = remTool.IsChecked == true;
                Settings.Section(r, "startup")["remember_last_folder"] = remFolder.IsChecked == true;
            });

            // ---- after a capture ----
            StackPanel af = NewPage(_i.T("settings.tab.after"));
            CheckBox copySave = Check(_i.T("settings.copy_and_save"), _s.CopyAndSave); af.Children.Add(copySave);
            CheckBox openFolder = Check(_i.T("settings.open_folder_after"), _s.OpenFolderAfterSave); af.Children.Add(openFolder);
            CheckBox sound = Check(_i.T("settings.sound"), _s.Sound); af.Children.Add(sound);
            // which sound and how loud: the standard Windows sounds, a test button plays the choice at once
            List<string> soundIds = NotifySound.Available();
            List<string> soundLabels = soundIds.Select(sid => NotifySound.Label(sid, _i)).ToList();
            Val<string> soundName = new Val<string> { V = _s.SoundName };
            TextBox soundVol = Input(_s.SoundVolume.ToString(), 60);
            Action playNow = delegate { int pv; if (!int.TryParse(soundVol.Text.Trim(), out pv)) pv = 100; NotifySound.Play(soundName.V, pv); };   // every change is heard at once
            af.Children.Add(Row(_i.T("settings.sound_choose"), Segment(soundIds, soundLabels, soundName, false, playNow)));
            sound.Checked += delegate { if (sound.IsLoaded) playNow(); };
            af.Children.Add(Row(_i.T("settings.sound_volume"), SliderBox(soundVol, 0, 100)));
            Border soundTest = Btn(_i.T("settings.sound_test"), false, delegate
            {
                int vv; if (!int.TryParse(soundVol.Text.Trim(), out vv)) vv = 100;
                NotifySound.Play(soundName.V, vv);
            });
            soundTest.HorizontalAlignment = HorizontalAlignment.Left;
            af.Children.Add(soundTest);
            af.Children.Add(Hint(_i.T("settings.sound_note")));
            CheckBox notify = Check(_i.T("settings.notifications"), _s.NotifyEnabled); af.Children.Add(notify);
            TextBox notifySec = Input(_s.NotifySeconds.ToString(), 60);
            af.Children.Add(Row(_i.T("settings.notification_seconds"), notifySec));
            CheckBox viewerOpen = Check(_i.T("settings.open_in_viewer"), _s.OpenInViewer); af.Children.Add(viewerOpen);
            CheckBox fsEditor = Check(_i.T("settings.fullscreen_in_editor"), _s.FullscreenInEditor); af.Children.Add(fsEditor);
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "after");
                o["copy_and_save"] = copySave.IsChecked == true;
                o["open_folder"] = openFolder.IsChecked == true;
                o["sound"] = sound.IsChecked == true;
                o["sound_name"] = soundName.V;
                PutInt(o, "sound_volume", soundVol, 0, 100);
                o["notifications"] = notify.IsChecked == true;
                int ns; if (int.TryParse(notifySec.Text.Trim(), out ns)) o["notification_seconds"] = Math.Max(1, Math.Min(30, ns));
                o["open_in_viewer"] = viewerOpen.IsChecked == true;
                o["fullscreen_in_editor"] = fsEditor.IsChecked == true;
            });

            // ---- sizes, snapping, strength of the held keys, Enter ----
            StackPanel sz = NewPage(_i.T("settings.tab.sizes"));
            sz.Children.Add(Hint(_i.T("settings.sizes_hint")));
            TextBox szMin = Input(_s.SizeMin.ToString(), 60), szMax = Input(_s.SizeMax.ToString(), 60), opMin = Input(_s.OpacityMin.ToString(), 60);
            sz.Children.Add(Row(_i.T("settings.size_min"), szMin, "settings.size_min.tip"));
            sz.Children.Add(Row(_i.T("settings.size_max"), szMax, "settings.size_min.tip"));
            sz.Children.Add(Row(_i.T("settings.opacity_min"), SliderBox(opMin, 0, 90), "settings.opacity_min.tip"));
            sz.Children.Add(new TextBlock { Text = _i.T("settings.pct_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 10, 0, 2) });
            sz.Children.Add(Hint(_i.T("settings.pct_hint")));
            TextBox pBrush = Input(_s.PctBrush, 120), pMarker = Input(_s.PctMarker, 120), pLine = Input(_s.PctLine, 120), pEraser = Input(_s.PctEraser, 120);
            sz.Children.Add(Row(_i.T("tools.brush"), pBrush, "settings.pct_tip"));
            sz.Children.Add(Row(_i.T("tools.marker"), pMarker, "settings.pct_tip"));
            sz.Children.Add(Row(_i.T("tools.line"), pLine, "settings.pct_tip"));
            sz.Children.Add(Row(_i.T("tools.eraser"), pEraser, "settings.pct_tip"));
            RegisterSizesDock(pBrush, pMarker, pLine, pEraser);

            sz.Children.Add(new TextBlock { Text = _i.T("settings.snap_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 14, 0, 2) });
            sz.Children.Add(Hint(_i.T("settings.snap_hint")));
            CheckBox snOn = Check(_i.T("settings.snap_enabled"), _s.SnapEnabled); sz.Children.Add(snOn);
            string[] snapKeys = { "right_alt", "right_ctrl", "right_shift", "left_alt", "left_ctrl", "left_shift" };
            List<string> snapLabels = new List<string>();
            foreach (string sk in snapKeys) snapLabels.Add(_i.T("settings.snapkey." + sk));
            Val<string> snKey = new Val<string> { V = _s.SnapKey };
            sz.Children.Add(Row(_i.T("settings.snap_key"), Segment(new List<string>(snapKeys), snapLabels, snKey)));
            TextBox snTh = Input(_s.SnapThreshold.ToString(), 60);
            sz.Children.Add(Row(_i.T("settings.snap_threshold"), SliderBox(snTh, 2, 40)));
            CheckBox snGuides = Check(_i.T("settings.snap_guides"), _s.SnapGuides); sz.Children.Add(snGuides);

            sz.Children.Add(new TextBlock { Text = _i.T("settings.strength_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 14, 0, 2) });
            sz.Children.Add(Hint(_i.T("settings.strength_hint")));
            TextBox stRound = Input(_s.StrengthRound.ToString(), 60), stCenter = Input(_s.StrengthCenter.ToString(), 60), stConstrain = Input(_s.StrengthConstrain.ToString(), 60),
                stAxis = Input(_s.StrengthAxis.ToString(), 60), stStep = Input(_s.StrengthStep.ToString(), 60);
            sz.Children.Add(Row(_i.T("settings.strength_round"), SliderBox(stRound, 0, 100)));
            sz.Children.Add(Row(_i.T("settings.strength_center"), SliderBox(stCenter, 0, 100)));
            sz.Children.Add(Row(_i.T("settings.strength_constrain"), SliderBox(stConstrain, 0, 100)));
            sz.Children.Add(Row(_i.T("settings.strength_axis"), SliderBox(stAxis, 0, 100)));
            sz.Children.Add(Row(_i.T("settings.strength_step"), SliderBox(stStep, 1, 25)));

            sz.Children.Add(new TextBlock { Text = _i.T("settings.enter_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 14, 0, 2) });
            sz.Children.Add(Hint(_i.T("settings.enter_hint")));
            string[] enterActs = { "save", "save_as", "pin", "copy", "upload", "close", "none" };
            List<string> enterActLabels = new List<string>();
            foreach (string ea in enterActs) enterActLabels.Add(_i.T("settings.enter.act." + ea));
            string[] enterCombos = { "none", "shift", "ctrl", "alt" };
            Dictionary<string, Val<string>> enterVals = new Dictionary<string, Val<string>>();
            foreach (string ec in enterCombos)
            {
                string cur; if (!_s.EnterActions.TryGetValue(ec, out cur) || string.IsNullOrWhiteSpace(cur)) cur = "none";
                Val<string> ev = new Val<string> { V = cur };
                enterVals[ec] = ev;
                sz.Children.Add(Row(_i.T("settings.enter.key." + ec), Segment(new List<string>(enterActs), enterActLabels, ev)));
            }

            sz.Children.Add(new TextBlock { Text = _i.T("settings.tray_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 14, 0, 2) });
            Val<string> trayShift = new Val<string> { V = _s.TrayShiftClick };
            sz.Children.Add(Row(_i.T("settings.tray_shift"), Segment(new List<string> { "settings", "region", "menu", "none" },
                new List<string> { _i.T("settings.tray_shift.settings"), _i.T("settings.tray_shift.region"), _i.T("settings.tray_shift.menu"), _i.T("settings.tray_shift.none") }, trayShift), "settings.tray_shift.tip"));
            _savers.Add(r =>
            {
                JsonObject ed = Settings.Section(r, "editor");
                PutInt(ed, "size_min", szMin, 1, 200); PutInt(ed, "size_max", szMax, 2, 400); PutInt(ed, "opacity_min", opMin, 0, 90);
                ed["pct_brush"] = pBrush.Text.Trim(); ed["pct_marker"] = pMarker.Text.Trim(); ed["pct_line"] = pLine.Text.Trim(); ed["pct_eraser"] = pEraser.Text.Trim();
                JsonObject sn = Settings.Section(r, "snap");
                sn["enabled"] = snOn.IsChecked == true; sn["key"] = snKey.V; PutInt(sn, "threshold_px", snTh, 2, 40); sn["show_guides"] = snGuides.IsChecked == true;
                JsonObject md2 = Settings.Section(r, "modifiers");
                PutInt(md2, "default_round_pct", stRound, 0, 100); PutInt(md2, "default_center_pct", stCenter, 0, 100); PutInt(md2, "default_constrain_pct", stConstrain, 0, 100);
                PutInt(md2, "default_axis_pct", stAxis, 0, 100); PutInt(md2, "wheel_step_pct", stStep, 1, 25);
                JsonObject en = Settings.Section(r, "enter_actions");
                foreach (KeyValuePair<string, Val<string>> kv in enterVals) en[kv.Key] = kv.Value.V;
                Settings.Section(r, "ui")["tray_shift_click"] = trayShift.V;
            });

            // ---- GIF and video ----
            StackPanel rc = NewPage(_i.T("settings.tab.record"));
            rc.Children.Add(Hint(_i.T("settings.record_hint")));
            TextBox recFolder = Input(_s.RecordFolder, 420);
            StackPanel recFolderRow = new StackPanel { Orientation = Orientation.Horizontal };
            recFolderRow.Children.Add(recFolder);
            recFolderRow.Children.Add(Btn(_i.T("settings.browse"), false, delegate
            {
                using (System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) recFolder.Text = d.SelectedPath;
                }
            }));
            rc.Children.Add(Row(_i.T("settings.record_folder"), recFolderRow, "settings.record_folder.tip"));
            CheckBox recFrame = Check(_i.T("settings.record_frame"), _s.RecordFrame); rc.Children.Add(recFrame);
            CheckBox recBtns = Check(_i.T("settings.record_editor_buttons"), _s.RecordEditorButtons); rc.Children.Add(recBtns);

            rc.Children.Add(new TextBlock { Text = "GIF", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 12, 0, 6) });
            TextBox gFps = Input(_s.GifFps.ToString(), 60), gMax = Input(_s.GifMaxSeconds.ToString(), 60), gScale = Input(_s.GifScale.ToString(), 60),
                gColors = Input(_s.GifColors.ToString(), 60), gLoop = Input(_s.GifLoop.ToString(), 60), gCount = Input(_s.GifCountdown.ToString(), 60);
            rc.Children.Add(Row(_i.T("settings.gif_fps"), SliderBox(gFps, 1, 30)));
            rc.Children.Add(Row(_i.T("settings.gif_max"), gMax, "settings.gif_max.tip"));
            rc.Children.Add(Row(_i.T("settings.gif_scale"), SliderBox(gScale, 10, 100), "settings.gif_scale.tip"));
            rc.Children.Add(Row(_i.T("settings.gif_colors"), gColors, "settings.gif_colors.tip"));
            rc.Children.Add(Row(_i.T("settings.gif_loop"), gLoop, "settings.gif_loop.tip"));
            rc.Children.Add(Row(_i.T("settings.rec_countdown"), gCount));
            CheckBox gDither = Check(_i.T("settings.gif_dither"), _s.GifDither); rc.Children.Add(gDither);
            CheckBox gCursor = Check(_i.T("settings.rec_cursor"), _s.GifCursor); rc.Children.Add(gCursor);
            CheckBox gMerge = Check(_i.T("settings.gif_merge"), _s.GifSkipDuplicates); rc.Children.Add(gMerge);
            CheckBox gCopy = Check(_i.T("settings.rec_copy_file"), _s.GifCopyFile); rc.Children.Add(gCopy);

            rc.Children.Add(new TextBlock { Text = _i.T("settings.video_title"), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 16, 0, 2) });
            rc.Children.Add(Hint(_i.T("settings.video_hint")));
            TextBox ffPath = Input(_s.FfmpegPath, 420);
            StackPanel ffRow = new StackPanel { Orientation = Orientation.Horizontal };
            ffRow.Children.Add(ffPath);
            ffRow.Children.Add(Btn(_i.T("settings.browse"), false, delegate
            {
                using (System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|*.exe|*.exe" })
                {
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) ffPath.Text = d.FileName;
                }
            }));
            rc.Children.Add(Row(_i.T("settings.ffmpeg_path"), ffRow, "settings.ffmpeg_path.tip"));
            TextBlock ffState = new TextBlock { Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, -6, 0, 12), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            Action ffCheck = delegate
            {
                JsonObject po = new JsonObject();
                Settings.Section(Settings.Section(po, "record"), "video")["ffmpeg_path"] = ffPath.Text.Trim();
                Settings probe = Settings.WithOverrides(po);
                string found = FfmpegRecorder.Find(probe);
                ffState.Text = found == null ? _i.T("settings.ffmpeg_missing") : _i.T("settings.ffmpeg_found") + " " + found;
            };
            ffPath.TextChanged += delegate { ffCheck(); };
            ffCheck();
            rc.Children.Add(ffState);
            TextBox vFps = Input(_s.VideoFps.ToString(), 60), vCrf = Input(_s.VideoCrf.ToString(), 60), vBit = Input(_s.VideoBitrateKbps.ToString(), 80),
                vMaxrate = Input(_s.VideoMaxrateKbps.ToString(), 80), vScale = Input(_s.VideoScale.ToString(), 60), vMax = Input(_s.VideoMaxSeconds.ToString(), 60),
                vCount = Input(_s.VideoCountdown.ToString(), 60), vExtra = Input(_s.VideoExtra, 420);
            Val<string> vCodec = new Val<string> { V = _s.VideoCodec };
            rc.Children.Add(Row(_i.T("settings.video_codec"), Segment(new List<string> { "libx264", "libx265", "h264_nvenc", "hevc_nvenc" },
                new List<string> { "H.264 (x264)", "H.265 (x265)", "H.264 NVIDIA", "H.265 NVIDIA" }, vCodec), "settings.video_codec.tip"));
            Val<string> vPreset = new Val<string> { V = _s.VideoPreset };
            rc.Children.Add(Row(_i.T("settings.video_preset"), Segment(new List<string> { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow" },
                new List<string> { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow" }, vPreset), "settings.video_preset.tip"));
            Val<string> vMode = new Val<string> { V = _s.VideoRateMode };
            // the mode decides which fields work: CRF mode uses only the CRF slider; bitrate mode uses the bitrate and its limit (the others are dimmed)
            Border crfFrame = Relevant(Row(_i.T("settings.video_crf"), SliderBox(vCrf, 0, 51), "settings.video_crf.tip"));
            Border bitFrame = Relevant(Row(_i.T("settings.video_bitrate"), vBit, "settings.video_bitrate.tip"));
            Border maxFrame = Relevant(Row(_i.T("settings.video_maxrate"), vMaxrate, "settings.video_maxrate.tip"));
            Action markRate = delegate { bool crfMode = vMode.V != "bitrate"; Mark(crfFrame, crfMode); Mark(bitFrame, !crfMode); Mark(maxFrame, !crfMode); };
            rc.Children.Add(Row(_i.T("settings.video_rate_mode"), Segment(new List<string> { "crf", "bitrate" },
                new List<string> { _i.T("settings.video_mode.crf"), _i.T("settings.video_mode.bitrate") }, vMode, false, markRate), "settings.video_rate_mode.tip"));
            rc.Children.Add(crfFrame); rc.Children.Add(bitFrame); rc.Children.Add(maxFrame);
            markRate();
            rc.Children.Add(Row(_i.T("settings.video_fps"), SliderBox(vFps, 5, 60)));
            rc.Children.Add(Row(_i.T("settings.gif_scale"), SliderBox(vScale, 10, 100), "settings.gif_scale.tip"));
            Val<string> vPix = new Val<string> { V = _s.VideoPixFmt };
            rc.Children.Add(Row(_i.T("settings.video_pixfmt"), Segment(new List<string> { "yuv420p", "yuv444p" },
                new List<string> { "yuv420p", "yuv444p" }, vPix), "settings.video_pixfmt.tip"));
            rc.Children.Add(Row(_i.T("settings.video_max"), vMax, "settings.video_max.tip"));
            rc.Children.Add(Row(_i.T("settings.rec_countdown"), vCount));
            Val<string> vAudio = new Val<string> { V = _s.VideoAudio };
            Val<string> vDev = new Val<string> { V = _s.VideoAudioDevice ?? "" };
            List<string> audioDevices = new List<string>();
            if (!string.IsNullOrEmpty(vDev.V)) audioDevices.Add(vDev.V);
            Border devFrame = null;
            Action markAudio = delegate { if (devFrame != null) Mark(devFrame, vAudio.V == "device"); };
            rc.Children.Add(Row(_i.T("settings.video_audio"), Segment(new List<string> { "none", "device" },
                new List<string> { _i.T("settings.video_audio.none"), _i.T("settings.video_audio.device") }, vAudio, false, markAudio)));
            TextBlock devState = new TextBlock { Foreground = Br(_t.TextSecondary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Action loadDevices = delegate
            {
                JsonObject po = new JsonObject();
                Settings.Section(Settings.Section(po, "record"), "video")["ffmpeg_path"] = ffPath.Text.Trim();
                string exe = FfmpegRecorder.Find(Settings.WithOverrides(po));
                if (exe == null) { devState.Text = _i.T("settings.ffmpeg_missing"); return; }
                devState.Text = _i.T("settings.video_audio_search");
                System.Threading.Tasks.Task.Run(() => ListAudioDevices(exe)).ContinueWith(tk => Dispatcher.BeginInvoke(new Action(delegate
                {
                    foreach (string d in tk.Result) if (!audioDevices.Contains(d)) audioDevices.Add(d);
                    devState.Text = tk.Result.Count == 0 ? _i.T("settings.video_audio_none_found") : "";
                })));
            };
            StackPanel devRow = new StackPanel { Orientation = Orientation.Horizontal };
            devRow.Children.Add(DropDown(vDev, () => audioDevices, 360, _i.T("settings.video_audio_default")));
            devRow.Children.Add(Btn(_i.T("settings.video_audio_refresh"), false, loadDevices));
            devRow.Children.Add(devState);
            devFrame = Relevant(Row(_i.T("settings.video_audio_device"), devRow, "settings.video_audio_device.tip"));
            rc.Children.Add(devFrame);
            markAudio();
            loadDevices();
            rc.Children.Add(Row(_i.T("settings.video_extra"), vExtra, "settings.video_extra.tip"));
            CheckBox vCursor = Check(_i.T("settings.rec_cursor"), _s.VideoCursor); rc.Children.Add(vCursor);
            CheckBox vCopy = Check(_i.T("settings.rec_copy_file"), _s.VideoCopyFile); rc.Children.Add(vCopy);
            _savers.Add(r =>
            {
                JsonObject rec = Settings.Section(r, "record");
                rec["folder"] = recFolder.Text.Trim(); rec["show_frame"] = recFrame.IsChecked == true; rec["editor_buttons"] = recBtns.IsChecked == true;
                JsonObject g = Settings.Section(rec, "gif");
                PutInt(g, "fps", gFps, 1, 30); PutInt(g, "max_seconds", gMax, 1, 600); PutInt(g, "scale_pct", gScale, 10, 100);
                PutInt(g, "colors", gColors, 2, 256); PutInt(g, "loop", gLoop, 0, 1000); PutInt(g, "countdown_seconds", gCount, 0, 30);
                g["dither"] = gDither.IsChecked == true; g["cursor"] = gCursor.IsChecked == true;
                g["merge_identical_frames"] = gMerge.IsChecked == true; g["copy_file_to_clipboard"] = gCopy.IsChecked == true;
                JsonObject v = Settings.Section(rec, "video");
                v["ffmpeg_path"] = ffPath.Text.Trim(); v["codec"] = vCodec.V; v["preset"] = vPreset.V; v["rate_mode"] = vMode.V;
                PutInt(v, "crf", vCrf, 0, 51); PutInt(v, "bitrate_kbps", vBit, 100, 200000); PutInt(v, "maxrate_kbps", vMaxrate, 0, 400000);
                PutInt(v, "fps", vFps, 5, 60); PutInt(v, "scale_pct", vScale, 10, 100); PutInt(v, "max_seconds", vMax, 0, 36000); PutInt(v, "countdown_seconds", vCount, 0, 30);
                v["pixel_format"] = vPix.V; v["audio"] = vAudio.V; v["audio_device"] = (vDev.V ?? "").Trim(); v["extra_args"] = vExtra.Text.Trim();
                v["cursor"] = vCursor.IsChecked == true; v["copy_file_to_clipboard"] = vCopy.IsChecked == true;
            });

            // ---- scrolling screenshot ----
            StackPanel sc = NewPage(_i.T("settings.tab.scroll"));
            sc.Children.Add(Hint(_i.T("settings.scroll_hint")));
            TextBox scDelay = Input(_s.ScrollDelayMs.ToString(), 70), scNotch = Input(_s.ScrollNotches.ToString(), 60), scMaxH = Input(_s.ScrollMaxHeight.ToString(), 80),
                scFrames = Input(_s.ScrollMaxFrames.ToString(), 60), scTop = Input(_s.ScrollSkipTop.ToString(), 60), scBottom = Input(_s.ScrollSkipBottom.ToString(), 60),
                scTol = Input(_s.ScrollTolerance.ToString(), 60), scStart = Input(_s.ScrollStartDelay.ToString(), 70);
            sc.Children.Add(Row(_i.T("settings.scroll_delay"), scDelay, "settings.scroll_delay.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_notches"), SliderBox(scNotch, 1, 15), "settings.scroll_notches.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_max_height"), scMaxH, "settings.scroll_max_height.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_max_frames"), scFrames));
            sc.Children.Add(Row(_i.T("settings.scroll_skip_top"), scTop, "settings.scroll_skip.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_skip_bottom"), scBottom, "settings.scroll_skip.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_tolerance"), SliderBox(scTol, 0, 30), "settings.scroll_tolerance.tip"));
            sc.Children.Add(Row(_i.T("settings.scroll_start"), scStart, "settings.scroll_start.tip"));
            CheckBox scCopy = Check(_i.T("settings.scroll_copy"), _s.ScrollCopy); sc.Children.Add(scCopy);
            CheckBox scSave = Check(_i.T("settings.scroll_save"), _s.ScrollSave); sc.Children.Add(scSave);
            CheckBox scShow = Check(_i.T("settings.scroll_show"), _s.ScrollShow); sc.Children.Add(scShow);
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "scroll");
                PutInt(o, "delay_ms", scDelay, 50, 5000); PutInt(o, "wheel_notches", scNotch, 1, 15); PutInt(o, "max_height_px", scMaxH, 500, 100000);
                PutInt(o, "max_frames", scFrames, 2, 500); PutInt(o, "skip_top_px", scTop, 0, 2000); PutInt(o, "skip_bottom_px", scBottom, 0, 2000);
                PutInt(o, "tolerance_pct", scTol, 0, 30); PutInt(o, "start_delay_ms", scStart, 0, 10000);
                o["copy_to_clipboard"] = scCopy.IsChecked == true; o["save_file"] = scSave.IsChecked == true; o["show_result"] = scShow.IsChecked == true;
            });

            // ---- keys inside the editor ----
            StackPanel ek = NewPage(_i.T("settings.tab.editor_keys"));
            ek.Children.Add(Hint(_i.T("settings.editor_keys_hint")));
            string[] keyIds = { "select", "arrow", "line", "rectangle", "ellipse", "brush", "marker", "text", "steps", "blur", "eraser", "picker", "fill", "spotlight", "magnifier",
                "preset1", "preset2", "preset3", "preset4", "preset5", "preset6", "preset7", "preset8", "preset9" };
            string[] keyDefs = { "V", "A", "L", "R", "O", "B", "M", "T", "N", "U", "E", "I", "F", "S", "Z", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            List<KeyValuePair<string, TextBox>> keyBoxes = new List<KeyValuePair<string, TextBox>>();
            // two columns: the tools on the left, the presets on the right (no scrolling needed)
            Grid keyCols = new Grid();
            keyCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel keyLeft = new StackPanel(), keyRight = new StackPanel();
            Grid.SetColumn(keyLeft, 0); Grid.SetColumn(keyRight, 1);
            keyCols.Children.Add(keyLeft); keyCols.Children.Add(keyRight);
            ek.Children.Add(keyCols);
            for (int i = 0; i < keyIds.Length; i++)
            {
                string cur; if (!_s.EditorKeys.TryGetValue(keyIds[i], out cur) || string.IsNullOrWhiteSpace(cur)) cur = keyDefs[i];
                TextBox box = Input(cur, 70);
                box.IsReadOnly = true;
                string def = keyDefs[i];
                box.PreviewKeyDown += delegate (object s2, KeyEventArgs e2)
                {
                    e2.Handled = true;
                    string kn = KeyName(e2, true);
                    if (kn != null) box.Text = kn;
                    else if (e2.Key == Key.Back || e2.Key == Key.Delete) box.Text = def;
                };
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(box);
                string label = keyIds[i].StartsWith("preset") ? _i.T("tip.preset") + " " + keyIds[i].Substring(6) : _i.T("tools." + keyIds[i]);
                row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text) });
                (keyIds[i].StartsWith("preset") ? keyRight : keyLeft).Children.Add(row);
                keyBoxes.Add(new KeyValuePair<string, TextBox>(keyIds[i], box));
            }
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "editor_keys");
                foreach (KeyValuePair<string, TextBox> kv in keyBoxes) o[kv.Key] = kv.Value.Text.Trim();
            });

            // ---- presets ----
            StackPanel ps = NewPage(_i.T("settings.tab.presets"));
            Val<string> pcount = new Val<string> { V = _s.PresetCount.ToString() };
            ps.Children.Add(Row(_i.T("settings.preset_count"), Segment(new List<string> { "2", "3", "4", "5", "6", "7", "8", "9" },
                new List<string> { "2", "3", "4", "5", "6", "7", "8", "9" }, pcount)));
            CheckBox shared = Check(_i.T("settings.shared_color"), _s.SharedColor);
            ps.Children.Add(shared);
            TextBox eraserR = Input(_s.EraserRadius.ToString(), 60);
            ps.Children.Add(Row(_i.T("settings.eraser_radius"), eraserR));
            ps.Children.Add(Hint(_i.T("settings.presets_hint")));
            StyleStore store = StyleStore.Create(_s);
            List<Action> presetApply = new List<Action>();
            foreach (EditorTool tool in StyleStore.Drawing)
            {
                EditorTool tl = tool;
                ps.Children.Add(new TextBlock { Text = _i.T("tools." + StyleStore.Key(tl)), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 10, 0, 3) });
                for (int pi = 0; pi < store.PresetCount; pi++)
                {
                    ToolStyle st = store.Preset(tl, pi);
                    StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    row.Children.Add(new TextBlock { Text = (pi + 1) + ":", Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.TextSecondary) });
                    string hex = "#" + st.Color.R.ToString("X2") + st.Color.G.ToString("X2") + st.Color.B.ToString("X2");
                    TextBox cBox; FrameworkElement cField = ColorField(hex, 80, "settings.preset_color.tip", out cBox);
                    TextBox sBox = Input(st.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), 50); TextBox oBox = Input(st.Opacity.ToString(), 50);
                    Tip(sBox, "settings.preset_size.tip"); Tip(oBox, "settings.preset_opacity.tip");
                    row.Children.Add(cField); row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(sBox); row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(oBox);
                    // text: font, bold, italic, alignment of this preset; arrow: head and its size
                    TextBox fontBox = null; CheckBox boldBox = null, italBox = null; Val<string> alignVal = null; Border alignBtn = null;
                    Val<string> headVal = null; Border headBtn = null; TextBox headScaleBox = null; CheckBox hollowBox = null;
                    if (tl == EditorTool.Text)
                    {
                        fontBox = Input(st.FontName, 110); Tip(fontBox, "settings.preset_font.tip");
                        Border fontPick = Btn("\u25BE", false, delegate { ShowFontsFor(fontBox); }); Tip(fontPick, "settings.preset_font.tip");
                        boldBox = new CheckBox { Content = "B", IsThreeState = true, IsChecked = st.Bold < 0 ? (bool?)null : st.Bold == 1, FontWeight = FontWeights.Bold, Foreground = Br(_t.Text), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        italBox = new CheckBox { Content = "I", IsThreeState = true, IsChecked = st.Italic < 0 ? (bool?)null : st.Italic == 1, FontStyle = FontStyles.Italic, Foreground = Br(_t.Text), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        Tip(boldBox, "settings.preset_bold.tip"); Tip(italBox, "settings.preset_bold.tip");
                        alignVal = new Val<string> { V = st.Align ?? "" };
                        alignBtn = Btn(AlignLabel(alignVal.V), false, null);
                        alignBtn.Width = 100; ((TextBlock)alignBtn.Child).HorizontalAlignment = HorizontalAlignment.Left;
                        Border ab = alignBtn;
                        alignBtn.MouseLeftButtonUp += delegate { alignVal.V = alignVal.V == "" ? "left" : alignVal.V == "left" ? "center" : alignVal.V == "center" ? "right" : alignVal.V == "right" ? "justify" : ""; ((TextBlock)ab.Child).Text = AlignLabel(alignVal.V); };
                        Tip(alignBtn, "settings.preset_align.tip");
                        row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(fontBox); row.Children.Add(fontPick); row.Children.Add(boldBox); row.Children.Add(italBox); row.Children.Add(alignBtn);
                    }
                    if (tl == EditorTool.Arrow)
                    {
                        headVal = new Val<string> { V = st.HeadStyle ?? "" };
                        headBtn = Btn(HeadLabel(headVal.V), false, null);
                        headBtn.Width = 118; ((TextBlock)headBtn.Child).HorizontalAlignment = HorizontalAlignment.Left;   // one width: the columns of all rows line up
                        Border hb = headBtn;
                        headBtn.MouseLeftButtonUp += delegate { headVal.V = headVal.V == "" ? "triangle" : headVal.V == "triangle" ? "open" : headVal.V == "open" ? "dot" : ""; ((TextBlock)hb.Child).Text = HeadLabel(headVal.V); };
                        Tip(headBtn, "settings.preset_head.tip");
                        headScaleBox = Input(st.HeadScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 50); Tip(headScaleBox, "settings.preset_head_scale.tip");
                        row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(headBtn); row.Children.Add(headScaleBox);
                        hollowBox = new CheckBox { Content = _i.T("settings.preset_head_hollow"), IsChecked = st.HeadHollow, Foreground = Br(_t.Text), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
                        Tip(hollowBox, "tip.head_fill");
                        row.Children.Add(hollowBox);
                    }
                    TextBox fBox = null; CheckBox rBox = null; TextBox tBox = null;
                    if (tl == EditorTool.Rect || tl == EditorTool.Ellipse)
                    {
                        fBox = Input(st.Fill.ToString(), 40); fBox.ToolTip = _i.T("props.fill") + " 0/1/2";
                        row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(fBox);
                    }
                    if (tl == EditorTool.Rect) { rBox = new CheckBox { Content = _i.T("props.rounded"), IsChecked = st.Rounded, Foreground = Br(_t.Text), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(rBox); }
                    if (tl == EditorTool.Text)
                    {
                        tBox = Input(st.TextStyle.ToString(), 40); tBox.ToolTip = _i.T("props.text_style") + " 0/1/2/3";
                        row.Children.Add(new TextBlock { Width = 6 }); row.Children.Add(tBox);
                    }
                    ToolStyle target = st;
                    presetApply.Add(delegate
                    {
                        try { target.Color = (WpfColor)ColorConverter.ConvertFromString(cBox.Text.Trim()); } catch { }
                        double sz; if (double.TryParse(sBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sz)) target.Size = Math.Max(1, Math.Min(96, sz));
                        int op; if (int.TryParse(oBox.Text.Trim(), out op)) target.Opacity = Math.Max(10, Math.Min(100, op));
                        int fv; if (fBox != null && int.TryParse(fBox.Text.Trim(), out fv)) target.Fill = Math.Max(0, Math.Min(2, fv));
                        if (rBox != null) target.Rounded = rBox.IsChecked == true;
                        int tv; if (tBox != null && int.TryParse(tBox.Text.Trim(), out tv)) target.TextStyle = Math.Max(0, Math.Min(3, tv));
                        if (fontBox != null) target.FontName = fontBox.Text.Trim();
                        if (boldBox != null) target.Bold = boldBox.IsChecked == null ? -1 : (boldBox.IsChecked == true ? 1 : 0);
                        if (italBox != null) target.Italic = italBox.IsChecked == null ? -1 : (italBox.IsChecked == true ? 1 : 0);
                        if (alignVal != null) target.Align = alignVal.V;
                        if (headVal != null) target.HeadStyle = headVal.V;
                        if (hollowBox != null) target.HeadHollow = hollowBox.IsChecked == true;
                        double hs; if (headScaleBox != null && double.TryParse(headScaleBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hs)) target.HeadScale = Math.Max(0.3, Math.Min(5, hs));
                    });
                    ps.Children.Add(row);
                }
            }
            _savers.Add(r =>
            {
                foreach (Action a in presetApply) a();
                r["presets"] = store.ToJson();
                JsonObject o = Settings.Section(r, "editor");
                o["preset_count"] = int.Parse(pcount.V);
                o["shared_color"] = shared.IsChecked == true;
                int er; if (int.TryParse(eraserR.Text.Trim(), out er)) o["eraser_radius"] = Math.Max(1, Math.Min(40, er));
            });

            // ---- looks of text, steps and arrows ----
            StackPanel lk = NewPage(_i.T("settings.tab.looks"));
            TextBox font = Input(_s.TextFont, 220);
            lk.Children.Add(Row(_i.T("settings.text_font"), BuildFontPicker(font)));   // a drop-down of your fonts, a filter over all (SettingsWindow.Fonts.cs)
            CheckBox allCaps = Check(_i.T("settings.text_all_caps"), _s.TextAllCaps); Tip(allCaps, "settings.text_all_caps.tip"); lk.Children.Add(allCaps);
            CheckBox bold = Check(_i.T("settings.text_bold"), _s.TextBold); lk.Children.Add(bold);
            CheckBox italic = Check(_i.T("settings.text_italic"), _s.TextItalic); lk.Children.Add(italic);
            Val<string> align = new Val<string> { V = _s.TextAlign };
            lk.Children.Add(Row(_i.T("settings.text_align"), Segment(new List<string> { "left", "center", "right", "justify" }, new List<string> { _i.T("layout.left"), _i.T("settings.center"), _i.T("layout.right"), _i.T("settings.justify") }, align)));
            TextBox plateColor = Input(_s.PlateColor, 100);
            lk.Children.Add(Row(_i.T("settings.plate_color"), plateColor));
            TextBox plateOp = Input(_s.PlateOpacity.ToString(), 60);
            lk.Children.Add(Row(_i.T("settings.plate_opacity"), plateOp));
            TextBox outlineW = Input(((int)Math.Round(_s.OutlineWidth * 100)).ToString(), 60);
            lk.Children.Add(Row(_i.T("settings.outline_width"), outlineW));
            Val<string> stepStyle = new Val<string> { V = _s.StepStyle };
            lk.Children.Add(Row(_i.T("settings.step_style"), Segment(new List<string> { "number", "upper", "lower", "roman" }, new List<string> { "1 2 3", "A B C", "a b c", "I II III" }, stepStyle)));
            TextBox stepStart = Input(_s.StepStart.ToString(), 60);
            lk.Children.Add(Row(_i.T("settings.step_start"), stepStart));
            Val<string> head = new Val<string> { V = _s.ArrowHead };
            lk.Children.Add(Row(_i.T("settings.arrow_head"), Segment(new List<string> { "triangle", "open", "dot" }, new List<string> { _i.T("settings.head.triangle"), _i.T("settings.head.open"), _i.T("settings.head.dot") }, head)));
            CheckBox arrowPanel = Check(_i.T("settings.arrow_panel"), _s.ArrowPanel); Tip(arrowPanel, "settings.arrow_panel.tip");
            lk.Children.Add(arrowPanel);
            TextBox rotStep = Input(_s.RotateStep.ToString(System.Globalization.CultureInfo.InvariantCulture), 60);
            lk.Children.Add(Row(_i.T("settings.rotate_step"), rotStep, "settings.rotate_step.tip"));
            RegisterLooksDock(font, allCaps, bold, italic, align, plateColor, plateOp, stepStyle, stepStart, head);   // the live picture at the right (SettingsWindow.Dock2.cs)
            _savers.Add(r =>
            {
                JsonObject ed = Settings.Section(r, "editor");
                ed["arrow_panel"] = arrowPanel.IsChecked == true;
                double rs; if (double.TryParse(rotStep.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rs)) ed["rotate_step"] = Math.Max(1, Math.Min(90, rs));
            });
            _savers.Add(r =>
            {
                JsonObject t = Settings.Section(r, "text");
                t["font"] = font.Text.Trim().Length == 0 ? "Segoe UI" : font.Text.Trim();
                t["all_caps"] = allCaps.IsChecked == true;
                t["bold"] = bold.IsChecked == true; t["italic"] = italic.IsChecked == true; t["align"] = align.V;
                t["plate_color"] = plateColor.Text.Trim().Length == 0 ? "auto" : plateColor.Text.Trim();
                int po; if (int.TryParse(plateOp.Text.Trim(), out po)) t["plate_opacity"] = Math.Max(10, Math.Min(100, po));
                int ow; if (int.TryParse(outlineW.Text.Trim(), out ow)) t["outline_width"] = Math.Max(5, Math.Min(50, ow)) / 100.0;
                JsonObject e2 = Settings.Section(r, "editor");
                e2["step_style"] = stepStyle.V; e2["arrow_head"] = head.V;
                int ss; if (int.TryParse(stepStart.Text.Trim(), out ss)) e2["step_start"] = ss;
            });

            // ---- profiles ----
            BuildBeautifyPage();                                     // beautiful screenshot (SettingsWindow.BeautifyPage.cs)
            BuildFitPage();                                          // panels for a big region (SettingsWindow.FitPage.cs)
            BuildRatioPage();                                        // aspect ratios on Tab (SettingsWindow.RatioPage.cs)
            StackPanel pf = NewPage(_i.T("settings.tab.profiles"));
            pf.Children.Add(Hint(_i.T("settings.profiles_hint")));
            pf.Children.Add(Hint(_i.T("settings.profile_edit_hint")));
            pf.Children.Add(Hint(_i.T("settings.baseline_explain")));

            // the key of the editor that walks through the profiles, and their order (the first one is used when a region is started)
            pf.Children.Add(Hint(_i.T("settings.profile_cycle_hint")));
            string curKey = string.IsNullOrWhiteSpace(_s.ProfileKey) ? "Oem3" : _s.ProfileKey;
            TextBox profKey = KeyBox(curKey);
            StackPanel keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            keyRow.Children.Add(profKey);
            keyRow.Children.Add(Btn(_i.T("settings.profile_key_default"), false, delegate { profKey.Text = "Oem3"; }));
            pf.Children.Add(Row(_i.T("settings.profile_cycle_key"), keyRow));

            List<string> cycle = _app.ProfileCycleOrder();
            string naturalOrder = string.Join(",", cycle);                          // the standard order: while it is not changed the saved setup stays the start
            bool ownOrder = _app.ProfileOrderIsOwn();
            StackPanel cycleHost = new StackPanel();
            Action rebuildCycle = null;
            rebuildCycle = delegate
            {
                cycleHost.Children.Clear();
                for (int idx = 0; idx < cycle.Count; idx++)
                {
                    int at = idx;
                    string pname = cycle[idx];
                    StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Tag = pname };
                    bool isCurrentSetup = false;
                    // a column at the left for the mark of the profile that is active now
                    line.Children.Add(new TextBlock { Text = pname == CurrentProfileKey() ? "●" : "", Width = 22, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Accent), ToolTip = _i.T("settings.profile_current") });
                    TextBlock nameLabel = new TextBlock { Text = "☰  " + (idx + 1) + ".  " + _app.ProfileLabel(pname), Width = 230, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), Cursor = Cursors.SizeAll, ToolTip = _i.T("tip.drag_row") };
                    DragReorder(nameLabel, line, cycleHost, cycle, rebuildCycle);          // drag the name: it stands between the others where it is dropped
                    line.Children.Add(nameLabel);
                    line.Children.Add(Btn("↑", false, delegate { if (at > 0) { string x = cycle[at]; cycle[at] = cycle[at - 1]; cycle[at - 1] = x; rebuildCycle(); } }));
                    line.Children.Add(Btn("↓", false, delegate { if (at < cycle.Count - 1) { string x = cycle[at]; cycle[at] = cycle[at + 1]; cycle[at + 1] = x; rebuildCycle(); } }));
                    if (!isCurrentSetup)
                    {
                        line.Children.Add(Btn(_i.T("settings.profile_apply"), false, delegate { int pg = _current; SaveQuiet(); _app.ApplyProfile(pname); ReopenAt(pg); }));
                        // the tools and the look now on the pages become this profile (it works for "Full" and the other built-in ones too)
                        line.Children.Add(Btn(_i.T("settings.profile_overwrite"), false, delegate { int pg = _current; SaveQuiet(); _app.SaveProfile(pname); ReopenAt(pg); }));
                        bool builtIn = Array.IndexOf(AppController.BuiltInProfiles, pname) >= 0;
                        bool rewritten = _s.ProfilesNode != null && _s.ProfilesNode[pname] != null;
                        if (!builtIn)
                            line.Children.Add(Btn(_i.T("actions.delete"), false, delegate { int pg = _current; SaveQuiet(); _app.DeleteProfile(pname); ReopenAt(pg); }));
                        else if (rewritten)
                            line.Children.Add(Btn(_i.T("settings.profile_restore"), false, delegate { int pg = _current; SaveQuiet(); _app.DeleteProfile(pname); ReopenAt(pg); }));
                    }
                    cycleHost.Children.Add(line);
                }
            };
            rebuildCycle();
            pf.Children.Add(cycleHost);
            _savers.Add(r =>
            {
                JsonObject pc = Settings.Section(r, "profile_cycle");
                pc["key"] = string.IsNullOrWhiteSpace(profKey.Text) ? "none" : profKey.Text.Trim();
                string nowOrder = string.Join(",", cycle);
                pc["order"] = (ownOrder || nowOrder != naturalOrder) ? nowOrder : "";   // not touched: the standard order, a capture starts with the saved setup
            });
            TextBox profName = Input("", 200);
            StackPanel saveRow = new StackPanel { Orientation = Orientation.Horizontal };
            saveRow.Children.Add(profName);
            saveRow.Children.Add(Btn(_i.T("settings.profile_save"), true, delegate
            {
                if (profName.Text.Trim().Length == 0) return;
                int pg = _current; SaveQuiet(); _app.SaveProfile(profName.Text); ReopenAt(pg);
            }));
            pf.Children.Add(Row(_i.T("settings.profile_new"), saveRow));
        }
    }
}
