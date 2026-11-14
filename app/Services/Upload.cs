using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Snapter
{
    internal sealed class UploadResult
    {
        public bool Ok;
        public string Link;     // a link to open (Google Drive); empty for a plain folder
        public string Path;     // where the file went (folder target)
        public string Message;  // the error text when Ok is false
    }

    /// <summary>The "where to upload" mechanism. Targets: none, folder (a normal folder, for example the one that
    /// "Google Drive for desktop" synchronizes) and gdrive (Google Drive API through OAuth). More targets can be added here.</summary>
    internal static class UploadService
    {
        public static bool Enabled(Settings s) { return s.UploadTarget == "folder" || s.UploadTarget == "gdrive"; }

        public static async Task<UploadResult> UploadAsync(Settings s, byte[] data, string fileName, string mime)
        {
            try
            {
                if (s.UploadTarget == "folder") return CopyToFolder(s, data, fileName);
                if (s.UploadTarget == "gdrive") return await new GDriveClient(s).UploadAsync(data, fileName, mime);
                return new UploadResult { Ok = false, Message = "no upload target" };
            }
            catch (Exception ex)
            {
                Log.Write("upload error: " + ex);
                return new UploadResult { Ok = false, Message = ex.Message };
            }
        }

        private static UploadResult CopyToFolder(Settings s, byte[] data, string fileName)
        {
            string folder = Environment.ExpandEnvironmentVariables(s.UploadFolderPath ?? "");
            if (string.IsNullOrWhiteSpace(folder)) return new UploadResult { Ok = false, Message = "upload folder is not set" };
            Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, fileName);
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName), ext = System.IO.Path.GetExtension(fileName);
            for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, stem + "_" + n + ext);
            File.WriteAllBytes(path, data);
            return new UploadResult { Ok = true, Path = path };
        }

        public static string MimeFor(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant().TrimStart('.'))
            {
                case "jpg": case "jpeg": return "image/jpeg";
                case "webp": return "image/webp";
                case "bmp": return "image/bmp";
                default: return "image/png";
            }
        }
    }

    /// <summary>PKCE (RFC 7636): the secret that protects the sign-in code of a desktop program.</summary>
    internal static class Pkce
    {
        public static string NewVerifier()
        {
            byte[] b = new byte[48];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return B64Url(b);
        }

        public static string Challenge(string verifier)
        {
            using (SHA256 sha = SHA256.Create()) return B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string B64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    /// <summary>Google Drive through the REST API v3 with OAuth 2.0 for desktop programs (loopback address + PKCE).
    /// Scope drive.file: the program sees only the files and folders it created itself. The refresh token is stored
    /// encrypted for the current Windows user (DPAPI) in config\gdrive.token; the client secret stays in the user's
    /// own settings file, never in the repository.</summary>
    internal sealed class GDriveClient
    {
        // addresses can be replaced (the self test points them at a local fake server)
        public static string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        public static string TokenUrl = "https://oauth2.googleapis.com/token";
        public static string ApiUrl = "https://www.googleapis.com/drive/v3";
        public static string UploadUrl = "https://www.googleapis.com/upload/drive/v3";
        public static string TokenFile = null; // null = config\gdrive.token
        public const string Scope = "https://www.googleapis.com/auth/drive.file";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private static string _access;
        private static DateTime _accessUntil = DateTime.MinValue;

        private readonly Settings _s;

        public GDriveClient(Settings s) { _s = s; }

        // ---------------------------------------------------------------- stored state
        private sealed class State { public string Refresh; public string FolderId; public string FolderName; }

        private static string StorePath { get { return TokenFile ?? System.IO.Path.Combine(Paths.Config, "gdrive.token"); } }

        private static State LoadState()
        {
            try
            {
                if (!File.Exists(StorePath)) return new State();
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), null, DataProtectionScope.CurrentUser);
                JsonObject o = JsonNode.Parse(Encoding.UTF8.GetString(raw)) as JsonObject;
                State st = new State();
                if (o != null)
                {
                    st.Refresh = (string)o["refresh"];
                    st.FolderId = (string)o["folder_id"];
                    st.FolderName = (string)o["folder_name"];
                }
                return st;
            }
            catch (Exception ex) { Log.Write("gdrive token read error: " + ex.Message); return new State(); }
        }

        private static void SaveState(State st)
        {
            JsonObject o = new JsonObject { ["refresh"] = st.Refresh, ["folder_id"] = st.FolderId, ["folder_name"] = st.FolderName };
            byte[] raw = ProtectedData.Protect(Encoding.UTF8.GetBytes(o.ToJsonString()), null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath));
            File.WriteAllBytes(StorePath, raw);
        }

        public bool SignedIn { get { return !string.IsNullOrEmpty(LoadState().Refresh); } }

        public void SignOut()
        {
            _access = null; _accessUntil = DateTime.MinValue;
            try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch (Exception ex) { Log.Write("gdrive sign out error: " + ex.Message); }
        }

        // ---------------------------------------------------------------- sign in (browser, loopback, PKCE)
        public static string BuildAuthUrl(string clientId, string redirect, string challenge, string state)
        {
            return AuthUrl + "?client_id=" + Uri.EscapeDataString(clientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&response_type=code&scope=" + Uri.EscapeDataString(Scope)
                + "&code_challenge=" + challenge + "&code_challenge_method=S256"
                + "&state=" + state + "&access_type=offline&prompt=consent";
        }

        /// <summary>Opens the browser (openBrowser), waits for Google to send the user back to the local address,
        /// swaps the code for tokens and keeps the refresh token. Throws on a refusal or a timeout.</summary>
        public async Task SignInAsync(Action<string> openBrowser, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(_s.GdClientId)) throw new InvalidOperationException("Client ID is empty (Settings - Cloud)");
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try
            {
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                string redirect = "http://127.0.0.1:" + port + "/";
                string verifier = Pkce.NewVerifier();
                string state = Pkce.B64Url(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
                openBrowser(BuildAuthUrl(_s.GdClientId.Trim(), redirect, Pkce.Challenge(verifier), state));

                string code = await WaitForCode(l, state, timeout);
                Dictionary<string, string> form = new Dictionary<string, string>
                {
                    { "code", code }, { "client_id", _s.GdClientId.Trim() }, { "client_secret", (_s.GdClientSecret ?? "").Trim() },
                    { "redirect_uri", redirect }, { "grant_type", "authorization_code" }, { "code_verifier", verifier }
                };
                JsonObject tok = await PostForm(TokenUrl, form);
                string refresh = (string)tok["refresh_token"];
                if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Google did not return a refresh token");
                State st = LoadState();
                st.Refresh = refresh; st.FolderId = null; st.FolderName = null;
                SaveState(st);
                _access = (string)tok["access_token"];
                _accessUntil = DateTime.UtcNow.AddSeconds(((int?)tok["expires_in"] ?? 3600) - 60);
                Log.Write("gdrive signed in");
            }
            finally { l.Stop(); }
        }

        private static async Task<string> WaitForCode(TcpListener l, string state, TimeSpan timeout)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(timeout))
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await AcceptAsync(l, cts.Token); }
                    catch (OperationCanceledException) { throw new TimeoutException("Sign in timed out"); }
                    using (c)
                    {
                        NetworkStream ns = c.GetStream();
                        byte[] buf = new byte[8192];
                        int n = await ns.ReadAsync(buf, 0, buf.Length, cts.Token);
                        string req = Encoding.ASCII.GetString(buf, 0, n);
                        string line = req.Split('\n')[0].Trim();          // GET /?code=...&state=... HTTP/1.1
                        string[] parts = line.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "/";
                        int qi = target.IndexOf('?');
                        Dictionary<string, string> q = new Dictionary<string, string>();
                        if (qi >= 0)
                            foreach (string kv in target.Substring(qi + 1).Split('&'))
                            {
                                int eq = kv.IndexOf('=');
                                if (eq > 0) q[Uri.UnescapeDataString(kv.Substring(0, eq))] = Uri.UnescapeDataString(kv.Substring(eq + 1).Replace('+', ' '));
                            }
                        bool hasCode = q.ContainsKey("code") || q.ContainsKey("error");
                        string body = hasCode
                            ? "<html><body style='font-family:Segoe UI,sans-serif;padding:40px'><h2>SNAPter</h2><p>Signed in. You can close this tab.</p></body></html>"
                            : "<html><body></body></html>";
                        byte[] bb = Encoding.UTF8.GetBytes(body);
                        string head = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bb.Length + "\r\nConnection: close\r\n\r\n";
                        byte[] hb = Encoding.ASCII.GetBytes(head);
                        await ns.WriteAsync(hb, 0, hb.Length);
                        await ns.WriteAsync(bb, 0, bb.Length);
                        if (!hasCode) continue; // favicon and the like
                        string err;
                        if (q.TryGetValue("error", out err)) throw new InvalidOperationException("Google refused: " + err);
                        string st;
                        if (!q.TryGetValue("state", out st) || st != state) throw new InvalidOperationException("The sign-in answer does not match the request");
                        return q["code"];
                    }
                }
            }
        }

        private static async Task<TcpClient> AcceptAsync(TcpListener l, CancellationToken ct)
        {
            Task<TcpClient> t = l.AcceptTcpClientAsync();
            Task done = await Task.WhenAny(t, Task.Delay(Timeout.Infinite, ct));
            if (done != t) throw new OperationCanceledException(ct);
            return t.Result;
        }

        // ---------------------------------------------------------------- tokens
        private async Task<string> AccessToken()
        {
            if (!string.IsNullOrEmpty(_access) && DateTime.UtcNow < _accessUntil) return _access;
            State st = LoadState();
            if (string.IsNullOrEmpty(st.Refresh)) throw new InvalidOperationException("Not signed in to Google (Settings - Cloud)");
            Dictionary<string, string> form = new Dictionary<string, string>
            {
                { "client_id", (_s.GdClientId ?? "").Trim() }, { "client_secret", (_s.GdClientSecret ?? "").Trim() },
                { "refresh_token", st.Refresh }, { "grant_type", "refresh_token" }
            };
            JsonObject tok = await PostForm(TokenUrl, form);
            _access = (string)tok["access_token"];
            _accessUntil = DateTime.UtcNow.AddSeconds(((int?)tok["expires_in"] ?? 3600) - 60);
            return _access;
        }

        private static async Task<JsonObject> PostForm(string url, Dictionary<string, string> form)
        {
            using (HttpResponseMessage r = await Http.PostAsync(url, new FormUrlEncodedContent(form)))
            {
                string text = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Google: " + (int)r.StatusCode + " " + Brief(text));
                return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
        }

        private static string Brief(string t) { return t.Length > 300 ? t.Substring(0, 300) : t; }

        private static async Task<JsonObject> Send(HttpMethod m, string url, string token, HttpContent body)
        {
            using (HttpRequestMessage req = new HttpRequestMessage(m, url))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                if (body != null) req.Content = body;
                using (HttpResponseMessage r = await Http.SendAsync(req))
                {
                    string text = await r.Content.ReadAsStringAsync();
                    if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Google Drive: " + (int)r.StatusCode + " " + Brief(text));
                    return string.IsNullOrWhiteSpace(text) ? new JsonObject() : (JsonNode.Parse(text) as JsonObject ?? new JsonObject());
                }
            }
        }

        // ---------------------------------------------------------------- folder and upload
        /// <summary>The folder id from the settings, or the program's own folder (found by name, or created) remembered in the token file.</summary>
        private async Task<string> EnsureFolder(string token)
        {
            if (!string.IsNullOrWhiteSpace(_s.GdFolderId)) return _s.GdFolderId.Trim();
            string name = string.IsNullOrWhiteSpace(_s.GdFolderName) ? "SNAPter" : _s.GdFolderName.Trim();
            State st = LoadState();
            if (!string.IsNullOrEmpty(st.FolderId) && st.FolderName == name) return st.FolderId;

            string q = "mimeType='application/vnd.google-apps.folder' and trashed=false and name='" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
            JsonObject found = await Send(HttpMethod.Get, ApiUrl + "/files?fields=files(id,name)&q=" + Uri.EscapeDataString(q), token, null);
            JsonArray files = found["files"] as JsonArray;
            string id = null;
            if (files != null && files.Count > 0) id = (string)files[0]["id"];
            if (id == null)
            {
                JsonObject meta = new JsonObject { ["name"] = name, ["mimeType"] = "application/vnd.google-apps.folder" };
                JsonObject made = await Send(HttpMethod.Post, ApiUrl + "/files?fields=id", token, new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"));
                id = (string)made["id"];
            }
            st.FolderId = id; st.FolderName = name;
            SaveState(st);
            return id;
        }

        public async Task<UploadResult> UploadAsync(byte[] data, string fileName, string mime)
        {
            string token = await AccessToken();
            string folder = await EnsureFolder(token);

            string boundary = "qp" + Guid.NewGuid().ToString("N");
            JsonObject meta = new JsonObject { ["name"] = fileName, ["parents"] = new JsonArray(folder) };
            MultipartContent mp = new MultipartContent("related", boundary);
            mp.Add(new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"));
            ByteArrayContent media = new ByteArrayContent(data);
            media.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
            mp.Add(media);

            JsonObject made = await Send(HttpMethod.Post, UploadUrl + "/files?uploadType=multipart&fields=id,webViewLink", token, mp);
            string id = (string)made["id"];
            if (_s.GdPublic)
            {
                JsonObject perm = new JsonObject { ["role"] = "reader", ["type"] = "anyone" };
                await Send(HttpMethod.Post, ApiUrl + "/files/" + id + "/permissions", token, new StringContent(perm.ToJsonString(), Encoding.UTF8, "application/json"));
            }
            string link = (string)made["webViewLink"];
            if (string.IsNullOrEmpty(link)) link = "https://drive.google.com/file/d/" + id + "/view";
            Log.Write("gdrive uploaded " + fileName + " -> " + id);
            return new UploadResult { Ok = true, Link = link };
        }
    }
}
