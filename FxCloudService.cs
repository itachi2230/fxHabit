using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace FxHabit.Services
{
    public class FxCloudService
    {
        private static HttpClient _httpClient;
        private const string TokenFileName = "session.bin";
        private readonly string _localProfileCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        private readonly string _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        public readonly string _sessionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_v1.json");
        private static readonly object _logLock = new object();
        public string CurrentToken { get; private set; }
        public string RefreshToken { get; private set; }
        public string AppId { get; private set; }

        public FxCloudService()
        {
            InitializeService();
        }

        #region CONFIGURATION ET INITIALISATION

        private void InitializeService()
        {
            string serverUrl = LoadConfiguration();

            if (_httpClient == null)
            {
                _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            LoadTokens();
            if (!Directory.Exists(_localProfileCache)) Directory.CreateDirectory(_localProfileCache);
        }

        private string LoadConfiguration()
        {
            string defaultUrl = "https://fxdataedge.com/api/public/index.php/";
            AppId = "FX_HABIT_DEFAULT";

            if (File.Exists(_configFilePath))
            {
                var lines = File.ReadAllLines(_configFilePath);
                foreach (var line in lines)
                {
                    string cleanLine = line.Trim();
                    if (cleanLine.StartsWith("server=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = cleanLine.Substring(7).Trim();
                        if (!string.IsNullOrEmpty(url)) defaultUrl = url.EndsWith("/") ? url : url + "/";
                    }
                    else if (cleanLine.StartsWith("app_id=", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = cleanLine.Substring(7).Trim();
                        if (!string.IsNullOrEmpty(id)) AppId = id;
                    }
                }
            }
            else
            {
                string configContent = "# CONFIGURATION FX-HABIT\nserver=https://fxdataedge.com/api/public/index.php/\napp_id=FX_HABIT_DEFAULT";
                File.WriteAllText(_configFilePath, configContent);
            }
            return defaultUrl;
        }

        #endregion

        #region AUTHENTIFICATION

        public async Task<string> RegisterAsync(string email, string phone, string password, string fullName, string bio, string imagePath)
        {
            string status = await GetCloudStatusAsync();
            if (status == "OFFLINE_NO_INTERNET") return "pas d'internet";
            if (status == "OFFLINE_SERVER_DOWN") return "serveur inaccessible";

            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(email ?? ""), "email");
                content.Add(new StringContent(password), "password");
                content.Add(new StringContent(fullName), "fullName");
                content.Add(new StringContent(phone ?? ""), "phone");
                content.Add(new StringContent(bio ?? ""), "bio");

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    var fileBytes = File.ReadAllBytes(imagePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                    content.Add(fileContent, "image", Path.GetFileName(imagePath));
                }

                var response = await _httpClient.PostAsync("api/register", content);

                if (response.IsSuccessStatusCode)
                {
                    await LoginAsync(email ?? phone, password);
                    return "yes";
                }
                return response.StatusCode == System.Net.HttpStatusCode.Conflict ? "email déjà utilisé" : "erreur serveur";
            }
            catch { return "erreur inconnue"; }
        }

        public async Task<string> LoginAsync(string identifier, string password)
        {
            string status = await GetCloudStatusAsync();
            if (status == "OFFLINE_NO_INTERNET") return "pas d'internet";
            if (status == "OFFLINE_SERVER_DOWN") return "serveur inaccessible";

            try
            {
                var data = new { identifier = identifier, password = password };
                var response = await _httpClient.PostAsync("api/login", GetJsonContent(data));

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        CurrentToken = doc.RootElement.GetProperty("token").GetString();
                        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh))
                            RefreshToken = refresh.GetString();
                    }
                    SaveTokens();
                    return "yes";
                }
                return response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "identifiants incorrects" : "erreur serveur";
            }
            catch { return "erreur inconnue"; }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(RefreshToken)) return false;
            try
            {
                var data = new { refresh_token = RefreshToken };
                var response = await _httpClient.PostAsync("token/refresh", GetJsonContent(data));
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        CurrentToken = doc.RootElement.GetProperty("token").GetString();
                        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh))
                            RefreshToken = refresh.GetString();
                    }
                    SaveTokens();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void Logout()
        {
            CurrentToken = null;
            RefreshToken = null;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path)) File.Delete(path);
            DeleteSessionFromDisk();
        }

        #endregion

        #region PROFIL ET SESSION

        public async Task<UserSessionData> GetProfileAsync()
        {
            if (string.IsNullOrEmpty(CurrentToken)) return null;
            try
            {
                var response = await SecureRequestAsync(() => _httpClient.GetAsync("api/me"));
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var session = JsonSerializer.Deserialize<UserSessionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    session.ImagePath = await DownloadProfileImageAsync(session.ImagePath);
                    SaveSessionToDisk(session.FullName, session.Email, session.Phone, session.Bio, session.ImagePath);
                    return session;
                }
            }
            catch { }
            return null;
        }

        public async Task<(bool success, string message, string newImagePath)> UpdateUserProfileAsync(string fullName, string phone, string bio, string localImagePath = null)
        {
            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(fullName ?? ""), "fullName");
                    content.Add(new StringContent(phone ?? ""), "phone");
                    content.Add(new StringContent(bio ?? ""), "bio");

                    if (!string.IsNullOrEmpty(localImagePath) && File.Exists(localImagePath))
                    {
                        var fileBytes = File.ReadAllBytes(localImagePath);
                        var fileContent = new ByteArrayContent(fileBytes);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                        content.Add(fileContent, "image", Path.GetFileName(localImagePath));
                    }

                    var response = await SecureRequestAsync(() => _httpClient.PostAsync("api/user/update", content));
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (var doc = JsonDocument.Parse(json))
                        {
                            string serverImg = doc.RootElement.GetProperty("imagePath").GetString();
                            string mail = doc.RootElement.GetProperty("email").GetString();
                            SaveSessionToDisk(fullName, mail, phone, bio, serverImg);
                            return (true, "Profil mis à jour !", serverImg);
                        }
                    }
                    return (false, $"Erreur: {response.StatusCode}", null);
                }
            }
            catch (Exception ex) { return (false, $"Erreur réseau : {ex.Message}", null); }
        }

        #endregion

        #region SYNCHRONISATION CLOUD

        public async Task<List<string>> FullSyncAsync()
        {
            var reports = new List<string>();
            Log("=== Démarrage FullSync ===");

            try
            {
                var uploadReports = await SyncEverythingAsync();
                foreach (var r in uploadReports)
                {
                    reports.Add(r);
                    Log($"Upload report: {r}");
                }

                var downloadResult = await SyncFromServerAsync();
                reports.Add(downloadResult);
                Log($"Download report: {downloadResult}");

                UpdateLocalLastSync(DateTime.Now);
            }
            catch (Exception ex)
            {
                Log($"CRITICAL SYNC ERROR: {ex.Message}");
                reports.Add("Erreur critique, voir log.txt");
            }

            Log("=== Fin FullSync ===");
            return reports;
        }
        public async Task<List<string>> SyncEverythingAsync()
        {
            var reports = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var item in GetAppSyncManifest())
            {
                string fullPath = Path.Combine(baseDir, item.LocalPath);
                if (item.IsDirectory && Directory.Exists(fullPath))
                {
                    foreach (var file in Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories))
                    {
                        string relative = file.Replace(fullPath, "").Replace("\\", "/").TrimStart('/');
                        string remote = Path.Combine(item.RemoteRelativePath, relative).Replace("\\", "/");
                        string res = await SyncFileAsync(file, remote);
                        reports.Add($"{Path.GetFileName(file)}: {res}");
                    }
                }
                else if (File.Exists(fullPath))
                {
                    string res = await SyncFileAsync(fullPath, item.RemoteRelativePath);
                    reports.Add($"{item.LocalPath}: {res}");
                }
            }
            return reports;
        }

        public async Task<string> SyncFromServerAsync()
        {
            try
            {
                var response = await SecureRequestAsync(() => _httpClient.GetAsync($"api/cloud/list?app_id={AppId}"));
                if (!response.IsSuccessStatusCode) return "Erreur liste serveur";

                var manifest = JsonSerializer.Deserialize<CloudManifest>(await response.Content.ReadAsStringAsync());
                int count = 0;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                foreach (var remote in manifest.files)
                {
                    string local = Path.Combine(baseDir, remote.path);
                    if (!File.Exists(local) || GetFileHash(local) != remote.hash)
                    {
                        if (await DownloadFileAsync(remote.path, local)) count++;
                    }
                }
                return $"Synchro : {count} fichiers mis à jour.";
            }
            catch (Exception ex) { return $"Erreur: {ex.Message}"; }
        }

        private async Task<string> SyncFileAsync(string localPath, string remotePath)
        {
            try
            {
                string hash = GetFileHash(localPath);

                // 1. Check file info
                var checkRes = await SecureRequestAsync(() => {
                    var content = new MultipartFormDataContent();
                    content.Add(new StringContent(AppId), "app_id");
                    content.Add(new StringContent(remotePath), "target_path");
                    return _httpClient.PostAsync("api/cloud/file-info", content);
                });

                if (checkRes.IsSuccessStatusCode)
                {
                    var json = await checkRes.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                        if (doc.RootElement.TryGetProperty("hash", out var sH) && sH.GetString() == hash) return "à jour";
                }

                // 2. Upload file
                var uploadRes = await SecureRequestAsync(() => {
                    var content = new MultipartFormDataContent();
                    content.Add(new StringContent(AppId), "app_id");
                    content.Add(new StringContent(remotePath), "target_path");
                    content.Add(new StringContent(hash), "file_hash");
                    var fileContent = new ByteArrayContent(File.ReadAllBytes(localPath));
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                    content.Add(fileContent, "file", Path.GetFileName(localPath));
                    return _httpClient.PostAsync("api/cloud/sync-file", content);
                });

                return uploadRes.IsSuccessStatusCode ? "success" : $"erreur {uploadRes.StatusCode}";
            }
            catch (Exception ex)
            {
                Log($"Erreur SyncFile: {ex.Message}");
                return "erreur: " + ex.Message;
            }
        }
        private async Task<bool> DownloadFileAsync(string remotePath, string localPath)
        {
            try
            {
                string url = $"api/cloud/download?app_id={AppId}&target_path={Uri.EscapeDataString(remotePath)}";
                var res = await SecureRequestAsync(() => _httpClient.GetAsync(url));
                if (res.IsSuccessStatusCode)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    using (var fs = new FileStream(localPath, FileMode.Create))
                        await res.Content.CopyToAsync(fs);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public async Task<string> DownloadProfileImageAsync(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string local = Path.Combine(_localProfileCache, fileName);
            if (File.Exists(local)) return local;

            try
            {
                var res = await _httpClient.GetAsync($"profiles/{fileName}");
                if (res.IsSuccessStatusCode)
                {
                    File.WriteAllBytes(local, await res.Content.ReadAsByteArrayAsync());
                    return local;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region UTILITAIRES ET RÉSEAU

        private async Task<HttpResponseMessage> SecureRequestAsync(Func<Task<HttpResponseMessage>> requestFunc)
        {
            SetAuthHeader();
            var res = await requestFunc();

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                Log("Token expiré, tentative de refresh...");

                // IMPORTANT: On attend la fin du refresh
                bool refreshed = await RefreshTokenAsync();

                if (refreshed)
                {
                    Log("Refresh réussi, nouvelle tentative de la requête...");
                    SetAuthHeader();

                    // On ré-exécute la requête originale. 
                    // ATTENTION: Si c'est un POST avec du contenu, 
                    // il faut que le 'requestFunc' recrée un nouveau contenu.
                    return await requestFunc();
                }
                else
                {
                    Log("Échec critique du refresh token. L'utilisateur doit se reconnecter.");
                    Logout(); // On vide les tokens pour éviter de boucler au prochain démarrage
                }
            }

            if (!res.IsSuccessStatusCode)
            {
                Log($"Requête échouée: {res.RequestMessage.RequestUri} | Status: {res.StatusCode}");
            }

            return res;
        }
        public static void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(logPath, logLine);
                }
            }
            catch
            {
                // On ne lève pas d'exception pour un log pour ne pas bloquer le logiciel
            }
        }
        private void SetAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization = !string.IsNullOrEmpty(CurrentToken)
                ? new AuthenticationHeaderValue("Bearer", CurrentToken) : null;
        }

        public async Task<string> GetCloudStatusAsync()
        {
            if (!await IsInternetAvailableAsync()) return "OFFLINE_NO_INTERNET";
            if (!await IsServerReachableAsync()) return "OFFLINE_SERVER_DOWN";
            return string.IsNullOrEmpty(CurrentToken) ? "ONLINE_NO_ACCOUNT" : "READY";
        }

        public async Task<bool> IsInternetAvailableAsync()
        {
            try { using (var p = new Ping()) return (await p.SendPingAsync("8.8.8.8", 2000)).Status == IPStatus.Success; }
            catch { return false; }
        }

        public async Task<bool> IsServerReachableAsync()
        {
            try { var res = await _httpClient.GetAsync("home"); return true; }
            catch { return false; }
        }

        private string GetFileHash(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        private StringContent GetJsonContent(object data)
            => new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

        public List<SyncItem> GetAppSyncManifest()
            => new List<SyncItem> { new SyncItem { LocalPath = "data/", RemoteRelativePath = "data/", IsDirectory = true } };

        #endregion

        #region GESTION DISQUE LOCALE

        public void SaveSessionToDisk(string name, string email, string phone, string bio, string imgPath)
        {
            try
            {
                var data = new UserSessionData { IsLoggedIn = true, FullName = name, Email = email, Phone = phone, Bio = bio, ImagePath = imgPath };
                File.WriteAllText(_sessionFilePath, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        public void UpdateLocalLastSync(DateTime date)
        {
            if (!File.Exists(_sessionFilePath)) return;
            try
            {
                var user = JsonSerializer.Deserialize<UserSessionData>(File.ReadAllText(_sessionFilePath));
                user.LastSyncDate = date;
                File.WriteAllText(_sessionFilePath, JsonSerializer.Serialize(user));
            }
            catch { }
        }

        public void DeleteSessionFromDisk() { if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath); }

        private void SaveTokens() { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName), new[] { CurrentToken ?? "", RefreshToken ?? "" }); }

        private void LoadTokens()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length > 0) CurrentToken = lines[0];
                if (lines.Length > 1) RefreshToken = lines[1];
            }
        }

        #endregion
    }

    #region CLASSES DE DONNÉES
    public class SyncItem { public string LocalPath { get; set; } public string RemoteRelativePath { get; set; } public bool IsDirectory { get; set; } }
    public class CloudManifest { public string app_id { get; set; } public List<CloudFileInfo> files { get; set; } }
    public class CloudFileInfo { public string path { get; set; } public string hash { get; set; } public long size { get; set; } public long last_modified { get; set; } }
    public class UserSession{ public string FullName { get; set; }public string Email { get; set; }public string Phone { get; set; }public string Bio { get; set; } public string LocalImagePath { get; set; } public DateTime? LastSyncDate { get; set; } }
    #endregion
}