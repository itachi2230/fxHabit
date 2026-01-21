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
        public DateTime? LastSyncDate { get; set; }
        public string ImagePath { get; set; }
    }

    public partial class SettingsView : UserControl
    {
        private FxCloudService _cloudService = new FxCloudService();
        private string selectedImagePath = "";
        public bool _isLoggedIn = false;
        private bool _isEditMode = false;
        private string _tempUpdateImagePath = "";
        // Chemin du fichier local (dans AppData pour être persistant)
        private readonly string _sessionFilePath  ;

        public SettingsView()
        {
            _sessionFilePath = _cloudService._sessionFilePath;
            InitializeComponent();
            LoadSessionFromDisk(); // Charger les données au démarrage
            ShowAppPanel();
        }

        

        private void HideAllPanels()
        {
            PanelApp.Visibility = Visibility.Collapsed;
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelProfile.Visibility = Visibility.Collapsed;
            PanelRegister.Visibility = Visibility.Collapsed;
        }

        private async Task ShowNotification(string message, bool isError = false, bool keepOpen = false)
        {
            Color themeColor = isError ? Color.FromRgb(255, 69, 69) : Color.FromRgb(0, 255, 255);
            SolidColorBrush themeBrush = new SolidColorBrush(themeColor);

            ToastText.Text = message.ToUpper();
            CyberToast.BorderBrush = themeBrush;
            ToastGlow.Color = themeColor;
            ToastIconCircle.Stroke = themeBrush;
            ToastIconPath.Stroke = themeBrush;

            // Icone : Sablier pour le chargement, Croix pour erreur, Check pour succès
            if (keepOpen && !isError)
                ToastIconPath.Data = Geometry.Parse("M 5,5 L 15,5 L 10,10 L 5,15 L 15,15"); // Simple Sablier
            else
                ToastIconPath.Data = isError ? Geometry.Parse("M 5,5 L 13,13 M 13,5 L 5,13") : Geometry.Parse("M 4,9 L 8,13 L 14,5");

            CyberToast.Opacity = 0;
            CyberToast.Visibility = Visibility.Visible;
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2));
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (!keepOpen)
            {
                await Task.Delay(3000);
                DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.5));
                fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
                CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        // Méthode pour forcer la fermeture
        private void HideNotification()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        private void BtnAppTab_Click(object sender, RoutedEventArgs e) => ShowAppPanel();
        private void BtnAccountTab_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();

        public void ShowAppPanel()
        {
            HideAllPanels();
            UpdateTabVisuals(false);
            PanelApp.Visibility = Visibility.Visible;
            TxtPassword.Clear();
        }

        public void ShowAccountPanel()
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
        
        public async void chargerprofile()
        {
            var profile = await _cloudService.GetProfileAsync();

            if (profile != null)
            {
                _isLoggedIn = true;
                LoadSessionFromDisk();
                // --- FIN DU CHARGEMENT (SUCCÈS) ---
                await ShowNotification("Connecté avec succès !");
                ShowAccountPanel();
            }
        }
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtIdentifier.Text) || string.IsNullOrWhiteSpace(TxtPassword.Password))
            {
                ShowNotification("Champs requis", true);
                return;
            }

            // --- DEBUT DU CHARGEMENT ---
            btnLogin.IsEnabled = false; // Désactive le bouton
            await ShowNotification("Connexion au serveur...", false, true); // Notification persistante
            // 1. Authentification pour obtenir le Token
            string success = await _cloudService.LoginAsync(TxtIdentifier.Text, TxtPassword.Password);

            if (success=="yes")
            {
                chargerprofile();
            }
            else
            {
                // --- FIN DU CHARGEMENT (ERREUR) ---
                await ShowNotification(success, true);
            }
            btnLogin.IsEnabled = true; // Réactive le bouton
        }
        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RegFullName.Text) || string.IsNullOrWhiteSpace(RegPassword.Password))
            {
                ShowNotification("Nom et mot de passe requis", true);
                return;
            }

            btnRegister.IsEnabled = false;

            await ShowNotification("Création du profil et envoi de l'image...", false, true); 
            string success = await _cloudService.RegisterAsync(RegEmail.Text, RegPhone.Text, RegPassword.Password, RegFullName.Text, RegBio.Text, selectedImagePath);

            if (success=="yes")
            {
                _isLoggedIn = true;

                // Sauvegarde immédiate dans le fichier local
                _cloudService.SaveSessionToDisk(RegFullName.Text, RegEmail.Text, RegPhone.Text, RegBio.Text, selectedImagePath);
                LoadSessionFromDisk(); // Met à jour l'UI avec les infos du fichier

                await ShowNotification("Compte créé !");
                btnRegister.IsEnabled = true;
                ShowAccountPanel();
            }
            else
            {
                ShowNotification(success, true);
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
           
            _isLoggedIn = false;
            // On vide les champs UI
            TxtSessionName.Text = "Utilisateur";
            TxtProfileEmail.Text = "---";

            ShowNotification("Session locale effacée");
            ShowAccountPanel();
        }
        // 1. Basculer entre Mode Vue et Mode Édition
        private void BtnEditToggle_Click(object sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;

            // Visibilité des textes vs inputs
            TxtSessionName.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditFullName.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            TxtProfilePhone.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditPhone.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            TxtProfileBio.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditBio.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            // Boutons et Image
            BtnSaveProfile.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
            EditPhotoOverlay.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            if (_isEditMode)
            {
                // Pré-remplir les champs avec les données actuelles
                EditFullName.Text = TxtSessionName.Text;
                EditPhone.Text = TxtProfilePhone.Text;
                EditBio.Text = TxtProfileBio.Text == "Aucune description disponible." ? "" : TxtProfileBio.Text;
                BtnEditToggle.Content = "ANNULER";
                _tempUpdateImagePath = ""; // Reset
            }
            else
            {
                BtnEditToggle.Content = "MODIFIER";
            }
        }

        // 2. Sélectionner une nouvelle photo durant l'édition
        private void SelectPhotoUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isEditMode) return;

            OpenFileDialog op = new OpenFileDialog { Filter = "Images (*.jpg;*.png)|*.jpg;*.png" };
            if (op.ShowDialog() == true)
            {
                _tempUpdateImagePath = op.FileName;
                ImgUserSession.ImageSource = new BitmapImage(new Uri(_tempUpdateImagePath));
            }
        }

        // 3. Sauvegarde finale vers le serveur
        private async void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            BtnSaveProfile.IsEnabled = false;
            await ShowNotification("Mise à jour du profil...", false, true);

            var result = await _cloudService.UpdateUserProfileAsync(
                EditFullName.Text,
                EditPhone.Text,
                EditBio.Text,
                _tempUpdateImagePath
            );

            if (result.success)
            {
                // On quitte le mode édition
                BtnEditToggle_Click(null, null);

                // On relance TA méthode pour rafraîchir l'UI proprement depuis le serveur
                chargerprofile();

                await ShowNotification("Profil mis à jour !");
            }
            else
            {
                await ShowNotification(result.message, true);
            }

            BtnSaveProfile.IsEnabled = true;
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