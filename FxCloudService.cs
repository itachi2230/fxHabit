using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace FxHabit.Services
{
    public class FxCloudService
    {
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };
        private const string TokenFileName = "session.bin"; // Fichier caché pour le token

        public string CurrentToken { get; private set; }

        public FxCloudService()
        {
            LoadToken();
        }

        // --- AUTHENTIFICATION ---

        public async Task<bool> RegisterAsync(string email, string phone, string password, string fullName, string bio, string imagePath)
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(email ?? ""), "email");
            content.Add(new StringContent(phone ?? ""), "phoneNumber");
            content.Add(new StringContent(password), "password");
            content.Add(new StringContent(fullName), "fullName");
            content.Add(new StringContent(bio ?? ""), "bio");

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                var fileContent = new ByteArrayContent(File.ReadAllBytes(imagePath));

                // Détection automatique de l'extension pour le Content-Type
                string extension = Path.GetExtension(imagePath).ToLower();
                string mimeType = extension == ".png" ? "image/png" : "image/jpeg";

                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
                content.Add(fileContent, "profilePicture", Path.GetFileName(imagePath));
            }

            var response = await _httpClient.PostAsync("api/register", content);
            return response.IsSuccessStatusCode;
        }
        public async Task<bool> LoginAsync(string identifier, string password)
        {
            // Note: LexikJWT attend par défaut "username" pour l'identifiant (email ou tel)
            var data = new { username = identifier, password = password };
            var response = await _httpClient.PostAsync("api/login", GetJsonContent(data));

            if (response.IsSuccessStatusCode)
            {
                var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                CurrentToken = result.RootElement.GetProperty("token").GetString();
                SaveToken(CurrentToken);
                return true;
            }
            return false;
        }


        public async Task<UserSessionData> GetProfileAsync()
        {
            if (string.IsNullOrEmpty(CurrentToken)) return null;

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken);
                var response = await _httpClient.GetAsync("api/me"); // Route à configurer sur ton Symfony

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    // On désérialise directement vers notre classe de données
                    return JsonSerializer.Deserialize<UserSessionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (Exception ex)
            {
            }
            return null;
        }
        // --- SYNCHRONISATION DES FICHIERS ---

        public async Task<bool> UploadFileAsync(string localFilePath, string appName)
        {
            if (string.IsNullOrEmpty(CurrentToken)) return false;

            string fileName = Path.GetFileName(localFilePath);
            byte[] fileData = File.ReadAllBytes(localFilePath);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken);

            // On envoie le fichier vers une route que nous allons créer en Symfony
            var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(fileData), "file", fileName);
            content.Add(new StringContent(appName), "app_name");

            var response = await _httpClient.PostAsync("api/sync/upload", content);
            return response.IsSuccessStatusCode;
        }

        // --- GESTION LOCALE DU TOKEN ---

        private void SaveToken(string token)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            // Idéalement, chiffrer ici avec ProtectedData
            File.WriteAllText(path, token);
        }

        private void LoadToken()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path))
            {
                CurrentToken = File.ReadAllText(path);
            }
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