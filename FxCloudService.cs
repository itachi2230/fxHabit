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
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };
        private const string TokenFileName = "session.bin";
        private readonly string _localProfileCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        public readonly string _sessionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_v1.json");
        // Propriétés publiques (inchangées pour ne pas casser Settings)
        public string CurrentToken { get; private set; }
        public string RefreshToken { get; private set; }

        public FxCloudService()
        {
            LoadTokens(); // On charge les deux jetons au démarrage
            if (!Directory.Exists(_localProfileCache)) Directory.CreateDirectory(_localProfileCache);
        }

        // --- AUTHENTIFICATION ---

        public async Task<string> RegisterAsync(string email, string phone, string password, string fullName, string bio, string imagePath)
        {
            // 1. Vérification préventive de la connectivité
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

                // Gestion de l'image de profil
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    var fileBytes = File.ReadAllBytes(imagePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("image/jpeg");
                    content.Add(fileContent, "image", Path.GetFileName(imagePath));
                }

                var response = await _httpClient.PostAsync("api/register", content);

                if (response.IsSuccessStatusCode)
                {
                    if (email != null) { await LoginAsync(email, password); }
                    else if (phone != null) { await LoginAsync(phone, password); }
                    else { }
                    
                    return "yes";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    return "email déjà utilisé";
                }
                else
                {
                    return "erreur serveur";
                }
            }
            catch (Exception)
            {
                return "erreur inconnue";
            }
        }
        public async Task<string> LoginAsync(string identifier, string password)
        {
            // 1. Vérification préventive de la connectivité
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

                        // Récupération sécurisée du refresh_token
                        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh))
                            RefreshToken = refresh.GetString();
                    }

                    SaveTokens();
                    return "yes";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return "identifiants incorrects";
                }
                else
                {
                    return "erreur serveur";
                }
            }
            catch (Exception)
            {
                return "erreur inconnue";
            }
        }
        // --- LOGIQUE REFRESH (INVISIBLE) ---

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

        // Méthode wrapper pour sécuriser les appels existants sans changer leur signature
        private async Task<HttpResponseMessage> SecureRequestAsync(Func<Task<HttpResponseMessage>> requestFunc)
        {
            SetAuthHeader();
            var response = await requestFunc();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                if (await RefreshTokenAsync())
                {
                    SetAuthHeader();
                    return await requestFunc();
                }
            }
            return response;
        }

        // --- PROFIL & MISE À JOUR (Utilisent maintenant le SecureRequest) ---
        public List<SyncItem> GetAppSyncManifest()
        {
            return new List<SyncItem>
            {
                new SyncItem { LocalPath = "data/", RemoteRelativePath = "data/", IsDirectory =true }
            };
        }
        public async Task<UserSessionData> GetProfileAsync()
        {
            if (string.IsNullOrEmpty(CurrentToken)) return null;

            try
            {
                var response = await SecureRequestAsync(() => _httpClient.GetAsync("api/me"));
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var session= JsonSerializer.Deserialize<UserSessionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    session.ImagePath=await DownloadProfileImageAsync(session.ImagePath);
                    return session;

                }
            }
            catch { }
            return null;
        }
        public async Task<List<string>> SyncEverythingAsync(string appId)
        {
            var manifest = GetAppSyncManifest();
            var reports = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var item in manifest)
            {
                string fullLocalPath = Path.Combine(baseDir, item.LocalPath);

                if (item.IsDirectory)
                {
                    if (Directory.Exists(fullLocalPath))
                    {
                        var files = Directory.GetFiles(fullLocalPath, "*.*", SearchOption.AllDirectories);

                        // On s'assure que le chemin du dossier finit par un slash pour Uri
                        string folderPathWithSlash = fullLocalPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                            ? fullLocalPath
                            : fullLocalPath + Path.DirectorySeparatorChar;

                        foreach (var file in files)
                        {
                            // Calcul manuel du chemin relatif compatible toutes versions .NET
                            Uri folderUri = new Uri(folderPathWithSlash);
                            Uri fileUri = new Uri(file);
                            string relativeFile = Uri.UnescapeDataString(folderUri.MakeRelativeUri(fileUri).ToString());

                            // On prépare le chemin pour le serveur (toujours des slashs /)
                            string remotePath = Path.Combine(item.RemoteRelativePath, relativeFile).Replace("\\", "/");

                            string result = await SyncFileAsync(appId, file, remotePath);
                            reports.Add($"{Path.GetFileName(file)}: {result}");
                        }
                    }
                
                }
                else
                {
                    // --- GESTION D'UN FICHIER SIMPLE ---
                    if (File.Exists(fullLocalPath))
                    {
                        string result = await SyncFileAsync(appId, fullLocalPath, item.RemoteRelativePath);
                        reports.Add($"{item.LocalPath}: {result}");
                    }
                    else
                    {
                        reports.Add($"{item.LocalPath}: introuvable en local");
                    }
                }
            }

            return reports;
        }
        public async Task<bool> UpdateUserAsync(string fullName, string phone, string bio, string imagePath = null)
        {
            if (string.IsNullOrEmpty(CurrentToken)) return false;

            var content = new MultipartFormDataContent();
            content.Add(new StringContent(fullName ?? ""), "fullName");
            content.Add(new StringContent(phone ?? ""), "phone");
            content.Add(new StringContent(bio ?? ""), "bio");

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                var fileContent = new ByteArrayContent(File.ReadAllBytes(imagePath));
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                content.Add(fileContent, "image", Path.GetFileName(imagePath));
            }

            var response = await SecureRequestAsync(() => _httpClient.PostAsync("api/user/update", content));
            return response.IsSuccessStatusCode;
        }

        // --- GESTION DES FICHIERS ---

        public async Task<string> DownloadProfileImageAsync(string serverFileName)
        {
            if (string.IsNullOrEmpty(serverFileName)) return null;
            string localPath = Path.Combine(_localProfileCache, serverFileName);
            if (File.Exists(localPath)) return localPath;

            try
            {
                var response = await _httpClient.GetAsync($"profiles/{serverFileName}");
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    File.WriteAllBytes(localPath, bytes);
                    return localPath;
                }
            }
            catch { }
            return null;
        }

        public async Task<bool> UploadSyncFileAsync(string localPath, string type)
        {
            if (string.IsNullOrEmpty(CurrentToken) || !File.Exists(localPath)) return false;

            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(File.ReadAllBytes(localPath));
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            content.Add(fileContent, "file", Path.GetFileName(localPath));
            content.Add(new StringContent(type), "type");
                    
            var response = await SecureRequestAsync(() => _httpClient.PostAsync("api/storage/sync", content));
            return response.IsSuccessStatusCode;
        }
        // Helper pour calculer l'empreinte du fichier (Hash)
        private string GetFileHash(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public async Task<string> SyncFileAsync(string appId, string localFilePath, string remoteRelativePath)
        {
            try
            {
                // 1. Connexion & Existence locale
                string status = await GetCloudStatusAsync();
                if (status == "OFFLINE_NO_INTERNET") return "pas d'internet";
                if (status == "OFFLINE_SERVER_DOWN") return "serveur inaccessible";
                if (!File.Exists(localFilePath)) return "fichier local introuvable";

                // 2. Calcul du Hash local
                string localHash = GetFileHash(localFilePath);

                // --- NOUVEAUTÉ : VÉRIFICATION PRÉALABLE ---
                var checkContent = new MultipartFormDataContent();
                checkContent.Add(new StringContent(appId), "app_id");
                checkContent.Add(new StringContent(remoteRelativePath), "target_path");

                // On appelle la route 'file-info' que nous avons créée dans le controller Symfony
                var checkResponse = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/file-info", checkContent));

                if (checkResponse.IsSuccessStatusCode)
                {
                    var infoJson = await checkResponse.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(infoJson))
                    {
                        if (doc.RootElement.TryGetProperty("hash", out var serverHash))
                        {
                            // SI LES HASH SONT IDENTIQUES, ON ARRÊTE TOUT ICI !
                            if (serverHash.GetString() == localHash)
                                return "déjà à jour";
                        }
                    }
                }

                // 3. Si on arrive ici, c'est que le fichier est différent ou n'existe pas
                var uploadContent = new MultipartFormDataContent();
                uploadContent.Add(new StringContent(appId), "app_id");
                uploadContent.Add(new StringContent(remoteRelativePath), "target_path");
                uploadContent.Add(new StringContent(localHash), "file_hash");

                var fileBytes = File.ReadAllBytes(localFilePath);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/octet-stream");
                uploadContent.Add(fileContent, "file", Path.GetFileName(localFilePath));

                // Envoi final
                var response = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/sync-file", uploadContent));

                if (response.IsSuccessStatusCode) return "success";

                return "erreur serveur (" + response.StatusCode + ")";
            }
            catch (Exception ex)
            {
                return "erreur: " + ex.Message;
            }
        }
        // --- UTILITAIRES & ÉTAT ---

        private void SetAuthHeader()
        {
            LoadTokens();
            _httpClient.DefaultRequestHeaders.Authorization = null; // Clean avant ajout
            if (!string.IsNullOrEmpty(CurrentToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken);
            }
        }

        private void SaveTokens()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            // On sauve les deux tokens sur deux lignes
            File.WriteAllLines(path, new[] { CurrentToken ?? "", RefreshToken ?? "" });
        }

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

        public void Logout()
        {
            CurrentToken = null;
            RefreshToken = null;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path)) File.Delete(path);
        }

        public async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        public async Task<bool> IsServerReachableAsync()
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(3000))
                {
                    var response = await _httpClient.GetAsync( "/home");
                    return true;
                }
            }
            catch { return false; }
        }

        public bool IsUserAuthenticated() => !string.IsNullOrEmpty(CurrentToken);

        public async Task<string> GetCloudStatusAsync()
        {
            if (!await IsInternetAvailableAsync()) return "OFFLINE_NO_INTERNET";
            if (!await IsServerReachableAsync()) return "OFFLINE_SERVER_DOWN";
            if (!IsUserAuthenticated()) return "ONLINE_NO_ACCOUNT";
            return "READY";
        }
        public void UpdateLocalLastSync(DateTime date)
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    var json = File.ReadAllText(_sessionFilePath);
                    var user = JsonSerializer.Deserialize<UserSessionData>(json);
                    user.LastSyncDate = date;

                    // On réécrit le fichier avec la date mise à jour
                    File.WriteAllText(_sessionFilePath, JsonSerializer.Serialize(user));
                }
            }
            catch { /* Gestion d'erreur silencieuse pour le cache */ }
        }
        private StringContent GetJsonContent(object data)
            => new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
    }
    public class SyncItem
    {
        public string LocalPath { get; set; }        // Chemin sur le PC
        public string RemoteRelativePath { get; set; } // Chemin dans le cloud
        public bool IsDirectory { get; set; }        // Est-ce un dossier complet ?
    }
}