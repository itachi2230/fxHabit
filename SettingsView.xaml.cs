using FxHabit.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json; // Intégré à .NET pour gérer le fichier local

namespace FxHabit
{
    // Classe simple pour stocker les données
    public class UserSessionData
    {
        public bool IsLoggedIn { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Bio { get; set; }
        public string ImagePath { get; set; }
    }

    public partial class SettingsView : UserControl
    {
        private FxCloudService _cloudService = new FxCloudService();
        private string selectedImagePath = "";
        private bool _isLoggedIn = false;

        // Chemin du fichier local (dans AppData pour être persistant)
        private readonly string _sessionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_v1.json");

        public SettingsView()
        {
            InitializeComponent();
            LoadSessionFromDisk(); // Charger les données au démarrage
            ShowAppPanel();
        }

        #region PERSISTENCE LOCALE (FICHIER)

        private void SaveSessionToDisk(string name, string email, string phone, string bio, string imgPath)
        {
            try
            {
                var session = new UserSessionData
                {
                    IsLoggedIn = true,
                    FullName = name,
                    Email = email,
                    Phone = phone,
                    Bio = bio,
                    ImagePath = imgPath
                };

                string json = JsonSerializer.Serialize(session);
                File.WriteAllText(_sessionFilePath, json);
            }
            catch (Exception ex) { Console.WriteLine("Erreur sauvegarde : " + ex.Message); }
        }

       
        private void DeleteSessionFromDisk()
        {
            if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath);
        }

        #endregion

        private void HideAllPanels()
        {
            PanelApp.Visibility = Visibility.Collapsed;
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelProfile.Visibility = Visibility.Collapsed;
            PanelRegister.Visibility = Visibility.Collapsed;
        }

        private async void ShowNotification(string message, bool isError = false)
        {
            Color themeColor = isError ? Color.FromRgb(255, 69, 69) : Color.FromRgb(0, 255, 255);
            SolidColorBrush themeBrush = new SolidColorBrush(themeColor);

            ToastText.Text = message.ToUpper();
            CyberToast.BorderBrush = themeBrush;
            ToastGlow.Color = themeColor;
            ToastIconCircle.Stroke = themeBrush;
            ToastIconPath.Stroke = themeBrush;
            ToastIconPath.Data = isError ? Geometry.Parse("M 5,5 L 13,13 M 13,5 L 5,13") : Geometry.Parse("M 4,9 L 8,13 L 14,5");

            CyberToast.Opacity = 0;
            CyberToast.Visibility = Visibility.Visible;
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2));
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            await Task.Delay(3000);
            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.5));
            fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void BtnAppTab_Click(object sender, RoutedEventArgs e) => ShowAppPanel();
        private void BtnAccountTab_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();

        private void ShowAppPanel()
        {
            HideAllPanels();
            UpdateTabVisuals(false);
            PanelApp.Visibility = Visibility.Visible;
        }

        private void ShowAccountPanel()
        {
            HideAllPanels();
            UpdateTabVisuals(true);

            if (_isLoggedIn)
            {
                PanelProfile.Visibility = Visibility.Visible;
                BtnLogout.Visibility = Visibility.Visible;
            }
            else
            {
                PanelLogin.Visibility = Visibility.Visible;
                BtnLogout.Visibility = Visibility.Collapsed;
            }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtIdentifier.Text) || string.IsNullOrWhiteSpace(TxtPassword.Password))
            {
                ShowNotification("Champs requis", true);
                return;
            }

            // 1. Authentification pour obtenir le Token
            bool success = await _cloudService.LoginAsync(TxtIdentifier.Text, TxtPassword.Password);

            if (success)
            {
                // 2. RÉCUPÉRATION DES INFOS DEPUIS LE SERVEUR
                var profile = await _cloudService.GetProfileAsync();

                if (profile != null)
                {
                    _isLoggedIn = true;

                    // 3. Sauvegarde locale des vraies infos reçues du serveur
                    SaveSessionToDisk(
                        profile.FullName,
                        profile.Email,
                        profile.Phone,
                        profile.Bio,
                        profile.ImagePath // Attention: ici c'est souvent une URL si ça vient du serveur
                    );

                    LoadSessionFromDisk(); // Met à jour l'UI (TxtProfileEmail, etc.)
                    ShowNotification("Session synchronisée");
                    ShowAccountPanel();
                }
                else
                {
                    ShowNotification("Erreur de synchronisation profil", true);
                }
            }
            else
            {
                ShowNotification("Identifiants invalides", true);
            }
        }
        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RegFullName.Text) || string.IsNullOrWhiteSpace(RegPassword.Password))
            {
                ShowNotification("Nom et mot de passe requis", true);
                return;
            }

            bool success = await _cloudService.RegisterAsync(RegEmail.Text, RegPhone.Text, RegPassword.Password, RegFullName.Text, RegBio.Text, selectedImagePath);

            if (success)
            {
                _isLoggedIn = true;

                // Sauvegarde immédiate dans le fichier local
                SaveSessionToDisk(RegFullName.Text, RegEmail.Text, RegPhone.Text, RegBio.Text, selectedImagePath);
                LoadSessionFromDisk(); // Met à jour l'UI avec les infos du fichier

                ShowNotification("Profil créé et sauvegardé");
                ShowAccountPanel();
            }
            else
            {
                ShowNotification("Erreur création", true);
            }
        }
        private void LoadSessionFromDisk()
        {
            if (!File.Exists(_sessionFilePath)) return;

            try
            {
                string json = File.ReadAllText(_sessionFilePath);
                var session = JsonSerializer.Deserialize<UserSessionData>(json);

                if (session != null && session.IsLoggedIn)
                {
                    _isLoggedIn = true;
                    TxtSessionName.Text = session.FullName;
                    TxtProfileEmail.Text = session.Email;
                    TxtProfilePhone.Text = session.Phone;
                    TxtProfileBio.Text = session.Bio;

                    if (!string.IsNullOrEmpty(session.ImagePath))
                    {
                        // Si c'est un chemin local et qu'il existe
                        if (File.Exists(session.ImagePath))
                        {
                            ImgUserSession.ImageSource = new BitmapImage(new Uri(session.ImagePath));
                        }
                        // Si c'est une URL (cas de secours)
                        else if (session.ImagePath.StartsWith("http"))
                        {
                            ImgUserSession.ImageSource = new BitmapImage(new Uri(session.ImagePath));
                        }
                    }
                }
            }
            catch { /* Erreur lecture */ }
        }
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            _cloudService.Logout();
            _isLoggedIn = false;
            DeleteSessionFromDisk(); // Efface le fichier local

            // On vide les champs UI
            TxtSessionName.Text = "Utilisateur";
            TxtProfileEmail.Text = "---";

            ShowNotification("Session locale effacée");
            ShowAppPanel();
        }

        private void ShowRegister_Click(object sender, RoutedEventArgs e)
        {
            HideAllPanels();
            PanelRegister.Visibility = Visibility.Visible;
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();

        private void SelectPhoto_Click(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog op = new OpenFileDialog { Filter = "Images (*.jpg;*.png)|*.jpg;*.png" };
            if (op.ShowDialog() == true)
            {
                selectedImagePath = op.FileName;
                ImgProfilePreview.ImageSource = new BitmapImage(new Uri(selectedImagePath));
            }
        }

        private void UpdateTabVisuals(bool isAccount)
        {
            BtnAccountTab.Foreground = isAccount ? Brushes.Cyan : Brushes.Gray;
            BtnAccountTab.BorderBrush = isAccount ? Brushes.Cyan : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            BtnAppTab.Foreground = !isAccount ? Brushes.Cyan : Brushes.Gray;
            BtnAppTab.BorderBrush = !isAccount ? Brushes.Cyan : new SolidColorBrush(Color.FromRgb(34, 34, 34));
        }
    }
}