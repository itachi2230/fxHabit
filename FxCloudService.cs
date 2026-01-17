using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;

namespace FxHabit.Services
{
    public class FxCloudService
    {
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };
        private const string TokenFileName = "session.bin";
        private readonly string _localProfileCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");

        public string CurrentToken { get; private set; }

        public FxCloudService()
        {
            LoadToken();
            if (!Directory.Exists(_localProfileCache)) Directory.CreateDirectory(_localProfileCache);
        }

        // --- AUTHENTIFICATION ---

        public async Task<bool> RegisterAsync(string email, string phone, string password, string fullName, string bio, string imagePath)
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(email ?? ""), "email");
            content.Add(new StringContent(password), "password");
            content.Add(new StringContent(fullName), "fullName");
            content.Add(new StringContent(phone ?? ""), "phone");
            content.Add(new StringContent(bio ?? ""), "bio");

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                var fileContent = new ByteArrayContent(File.ReadAllBytes(imagePath));
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                content.Add(fileContent, "image", Path.GetFileName(imagePath));
            }

            var response = await _httpClient.PostAsync("api/register", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> LoginAsync(string identifier, string password)
        {
            // Note: Symfony attend 'username' pour LexikJWT par défaut
            var data = new { identifier = identifier, password = password };
            var response = await _httpClient.PostAsync("api/login", GetJsonContent(data));

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonDocument.Parse(json);
                CurrentToken = result.RootElement.GetProperty("token").GetString();
                SaveToken(CurrentToken);
                return true;
            }
            return false;
        }

        // --- PROFIL & MISE À JOUR ---

        public async Task<UserSessionData> GetProfileAsync()
        {
            if (string.IsNullOrEmpty(CurrentToken)) return null;

            SetAuthHeader();
            try
            {
                var response = await _httpClient.GetAsync("api/me");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<UserSessionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch { }
            return null;
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

            SetAuthHeader();
            var response = await _httpClient.PostAsync("api/user/update", content);
            return response.IsSuccessStatusCode;
        }

        // --- GESTION DES FICHIERS (IMAGE & SYNC) ---

        public async Task<string> DownloadProfileImageAsync(string serverFileName)
        {
            if (string.IsNullOrEmpty(serverFileName) ) return null;

            string localPath = Path.Combine(_localProfileCache, serverFileName);
            if (File.Exists(localPath)) return localPath;

            try
            {
                // On télécharge depuis le dossier public du Symfony
                var response = await _httpClient.GetAsync($"profiles/{serverFileName}");
                if (response.IsSuccessStatusCode)
                {
                    if (!Directory.Exists(_localProfileCache)) Directory.CreateDirectory(_localProfileCache);
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    File.WriteAllBytes(localPath, bytes);
                    return localPath;
                }
                else { }
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
            content.Add(new StringContent(type), "type"); // 'data', 'logs', ou 'stats'

            SetAuthHeader();
            var response = await _httpClient.PostAsync("api/storage/sync", content);
            return response.IsSuccessStatusCode;
        }

        // --- UTILITAIRES ---

        private void SetAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken);
        }

        private void SaveToken(string token)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            File.WriteAllText(path, token);
        }

        private void LoadToken()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path)) CurrentToken = File.ReadAllText(path);
        }

        public void Logout()
        {
            CurrentToken = null;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path)) File.Delete(path);

            
        }

        private StringContent GetJsonContent(object data)
            => new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
    }
}