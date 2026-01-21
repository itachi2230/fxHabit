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
            string defaultUrl = "http://localhost:8080/";
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
                string configContent = "# CONFIGURATION FX-HABIT\nserver=http://localhost:8080/\napp_id=FX_HABIT_DEFAULT";
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
            var reports = new List<string> { $"--- Début synchro ({DateTime.Now:HH:mm}) ---" };
            try
            {
                reports.Add("Envoi des modifications locales...");
                var uploadReports = await SyncEverythingAsync();
                reports.AddRange(uploadReports);

                reports.Add("Récupération des fichiers distants...");
                var downloadResult = await SyncFromServerAsync();
                reports.Add(downloadResult);

                UpdateLocalLastSync(DateTime.Now);
                reports.Add("--- Synchronisation terminée ---");
            }
            catch (Exception ex) { reports.Add($"!!! Erreur : {ex.Message}"); }
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
                var checkContent = new MultipartFormDataContent();
                checkContent.Add(new StringContent(AppId), "app_id");
                checkContent.Add(new StringContent(remotePath), "target_path");

                var check = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/file-info", checkContent));
                if (check.IsSuccessStatusCode)
                {
                    using (var doc = JsonDocument.Parse(await check.Content.ReadAsStringAsync()))
                        if (doc.RootElement.TryGetProperty("hash", out var sHash) && sHash.GetString() == hash) return "à jour";
                }

                var upload = new MultipartFormDataContent();
                upload.Add(new StringContent(AppId), "app_id");
                upload.Add(new StringContent(remotePath), "target_path");
                upload.Add(new StringContent(hash), "file_hash");
                upload.Add(new ByteArrayContent(File.ReadAllBytes(localPath)), "file", Path.GetFileName(localPath));

                var res = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/sync-file", upload));
                return res.IsSuccessStatusCode ? "success" : "erreur serveur";
            }
            catch (Exception ex) { return "erreur: " + ex.Message; }
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
                if (await RefreshTokenAsync())
                {
                    SetAuthHeader();
                    return await requestFunc();
                }
            }
            return res;
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
    #endregion
}