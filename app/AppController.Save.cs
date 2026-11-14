using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;

namespace Snapter
{
    // Upload, Google Drive and "Save as" - moved out of AppController.cs (800-line limit).
    internal sealed partial class AppController
    {
        /// <summary>Encodes the picture at once (the caller may dispose it afterwards) and sends it to the chosen target in the background.</summary>
        public async void UploadBitmap(Bitmap bmp)
        {
            if (!UploadService.Enabled(Settings))
            {
                Notify(I18n.T("msg.upload_off"));
                return;
            }
            Settings st = Settings;
            byte[] data; string ext, name;
            try
            {
                data = ImageOutput.Encode(bmp, st, st.Format, out ext);
                name = ImageOutput.ExpandTemplate(st.FileNameTemplate, DateTime.Now);
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                name += "." + ext;
            }
            catch (Exception ex) { Fail(ex); return; }

            Notify(I18n.T("msg.uploading", name));
            UploadResult r = await UploadService.UploadAsync(st, data, name, UploadService.MimeFor(ext));
            try
            {
                if (!r.Ok) { Notify(I18n.T("msg.upload_failed", r.Message), true); return; }
                string what = !string.IsNullOrEmpty(r.Link) ? r.Link : r.Path;
                if (st.UploadCopyLink && !string.IsNullOrEmpty(what))
                {
                    try { System.Windows.Forms.Clipboard.SetText(what); } catch (Exception ex) { Log.Write("clipboard error: " + ex.Message); }
                }
                Notify(I18n.T(!string.IsNullOrEmpty(r.Link) ? "msg.uploaded_link" : "msg.uploaded_path", what));
                Log.Write("uploaded: " + what);
            }
            catch (Exception ex) { Fail(ex); }
        }

        public bool GDriveSignedIn { get { return new GDriveClient(Settings).SignedIn; } }

        /// <summary>Opens the browser, waits for the sign-in and reports the result as a balloon. Returns an error text or null.</summary>
        public async Task<string> GDriveSignIn()
        {
            try
            {
                await new GDriveClient(Settings).SignInAsync(url =>
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }, TimeSpan.FromMinutes(3));
                Notify(I18n.T("msg.gdrive_signed_in"));
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("gdrive sign in error: " + ex.Message);
                return ex.Message;
            }
        }

        public void GDriveSignOut() { new GDriveClient(Settings).SignOut(); }

        /// <summary>A small test picture goes to the chosen target, to check that everything works.</summary>
        public void UploadTestImage()
        {
            using (Bitmap b = new Bitmap(320, 120))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(45, 140, 255));
                using (System.Drawing.Font f = new System.Drawing.Font("Segoe UI", 22, System.Drawing.FontStyle.Bold))
                    g.DrawString("SNAPter test", f, Brushes.White, 20, 36);
                UploadBitmap(b);
            }
        }

        private void SaveAs(Bitmap bmp)
        {
            string folder = Environment.ExpandEnvironmentVariables(Settings.SaveFolder);
            try { Directory.CreateDirectory(folder); } catch { }
            Log.Write("save as: dialog opens");
            // a hidden top-most owner: the dialog always comes to the front, even when another window is active
            using (System.Windows.Forms.Form owner = new System.Windows.Forms.Form { TopMost = true, ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-32000, -32000), Size = new System.Drawing.Size(1, 1), FormBorderStyle = System.Windows.Forms.FormBorderStyle.None })
            using (System.Windows.Forms.SaveFileDialog d = new System.Windows.Forms.SaveFileDialog())
            {
                owner.Show();
                d.Filter = "PNG|*.png|JPG|*.jpg|WebP|*.webp|BMP|*.bmp";
                d.FilterIndex = Math.Max(0, Array.IndexOf(ImageOutput.Formats, ImageOutput.NormalizeFormat(Settings.Format))) + 1;
                if (Settings.RememberFolder && !string.IsNullOrEmpty(Settings.LastFolder) && Directory.Exists(Settings.LastFolder)) folder = Settings.LastFolder; // the folder of the last "Save as"
                d.InitialDirectory = folder;
                string name = ImageOutput.ExpandTemplate(Settings.FileNameTemplate, DateTime.Now);
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                d.FileName = name;
                d.AddExtension = true;
                if (d.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) { Log.Write("save as: cancelled"); return; }
                ImageOutput.SaveTo(bmp, d.FileName, Settings);
                Notify(I18n.T("msg.saved_to", d.FileName), false, d.FileName);
                Log.Write("region saved as " + d.FileName);
                AfterSaved(bmp, d.FileName);
                if (Settings.RememberFolder)
                {
                    string dir = Path.GetDirectoryName(d.FileName);
                    try { Settings.PatchUser(r => { Settings.Section(r, "state")["last_folder"] = dir; }); Settings.LastFolder = dir; }
                    catch (Exception ex) { Log.Write("last folder save error: " + ex.Message); }
                }
            }
        }
    }
}
