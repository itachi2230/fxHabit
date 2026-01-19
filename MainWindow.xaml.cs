using FxHabit.Services;
using LiveCharts;
using LiveCharts.Wpf;
using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FxHabit
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        MainViewModel vm = new MainViewModel();
        private FxCloudService _cloudService = new FxCloudService();
        private readonly string _sessionFilePath;
        public MainWindow()
        {
            _sessionFilePath = _cloudService._sessionFilePath;
            InitializeComponent();
            LoadUserProfile();
            this.DataContext = vm;
        }
        public async Task ShowNotification(string message, bool isError = false, bool keepOpen = false,double secondes=0.2)
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
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(secondes));
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (!keepOpen)
            {
                await Task.Delay(3000);
                DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.5));
                fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
                CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
        private void BtnAddHabit_Click(object sender, RoutedEventArgs e)
        {
            AddHabitWindow addWin = new AddHabitWindow();
            addWin.Owner = this;

            if (addWin.ShowDialog() == true)
            {
                // VERIFICATION DE SECURITÉ
                if (vm == null)
                {
                    MessageBox.Show("Erreur : Le ViewModel n'est pas lié à la fenêtre.");
                    return;
                }

                if (addWin.NewHabit != null)
                {
                    // Si la liste est nulle (sécurité supplémentaire)
                    if (vm.HabitList == null)
                        vm.HabitList = new ObservableCollection<Habit>();

                    // On ajoute à la liste visuelle
                    vm.HabitList.Add(addWin.NewHabit);

                    // On sauvegarde via ton nouvel outil
                    HabitStorage.SaveSingleHabit(addWin.NewHabit);
                    // 3. ON FORCE LE DASHBOARD À SE METTRE À JOUR
                    vm.RefreshData();
                }
            }
        }
        private void DeleteHabit_Click(object sender, RoutedEventArgs e)
        {
        var btn = sender as Button;
        var habit = btn.DataContext as Habit;

        if (habit != null)
        {
            var result = MessageBox.Show($"Supprimer la mission '{habit.Title}' ?", "Confirmation", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                var vm = (MainViewModel)this.DataContext;
                vm.HabitList.Remove(habit);
                HabitStorage.DeleteHabit(habit.Id);
            }
        }
        }
        private void LoadUserProfile()
        {
            // 1. VERIFICATION DU FICHIER LOCAL (INSTANTANÉ)
            if (File.Exists(_sessionFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_sessionFilePath);
                    var session = JsonSerializer.Deserialize<UserSessionData>(json);

                    if (session != null && session.IsLoggedIn)
                    {
                        ApplyUserInterface(session);
                        return; // On a affiché le profil local, on s'arrête là pour l'instant
                    }
                }
                catch { /* Fichier corrompu ou illisible */ }
            }

            // 2. SI PAS DE FICHIER OU PAS CONNECTÉ
            CloudConnectedPanel.Visibility = Visibility.Collapsed;
            CloudDisconnectedPanel.Visibility = Visibility.Visible;
        }

        // Cette méthode met à jour l'UI avec les données qu'on lui donne (locales ou serveurs)
        private void ApplyUserInterface(UserSessionData data)
        {
            CloudConnectedPanel.Visibility = Visibility.Visible;
            CloudDisconnectedPanel.Visibility = Visibility.Collapsed;

            TxtUserName.Text = data.FullName.ToUpper();
            if (data.LastSyncDate.HasValue)
            {
                // Calcul du temps écoulé
                TimeSpan diff = DateTime.Now - data.LastSyncDate.Value;
                string timeAgo = FormatTimeAgo(diff);
                TxtLastSync.Text = $"Synchro : {timeAgo}";
            }
            else
            {
                TxtLastSync.Text = "Jamais synchro";
            }
            // Chargement de l'image (Gestion du chemin local vs URL)
            if (!string.IsNullOrEmpty(data.ImagePath) && File.Exists(data.ImagePath))
            {
                UserProfileImage.ImageSource = new BitmapImage(new Uri(data.ImagePath));
            }
            else
            {
                // Image par défaut si le chemin n'existe plus
                UserProfileImage.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/default_user.png"));
            }
        }
        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            // 1. On récupère le bouton, puis l'icône à l'intérieur
            var btn = sender as Button;
            var icon = btn?.Content as PackIcon;

            if (icon == null) return; // Sécurité

            // 2. Préparation de l'animation de rotation
            var rotateTransform = new RotateTransform();
            icon.RenderTransform = rotateTransform;
            icon.RenderTransformOrigin = new Point(0.5, 0.5);

            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Lancement de l'animation
            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
            btn.IsEnabled = false; // On désactive le bouton pendant la sync

            try
            {
                // 2. Lancer la synchronisation
                List<string> results = await _cloudService.FullSyncAsync(HabitStorage.appid);

                // 3. Analyse intelligente des résultats
                // On vérifie si une ligne contient "success" ou "mis à jour"
                int changeCount = results.Count(line => line.Contains("success") || line.Contains("mis à jour") && !line.Contains("0"));
                bool hasCriticalError = results.Any(line => line.Contains("!!! Erreur") || line.Contains("inaccessible"));

                // Construction du message de notification
                string messageFinal;
                bool isError = hasCriticalError;

                if (hasCriticalError)
                {
                    messageFinal = "La synchronisation a échoué. Vérifiez votre connexion.";
                }
                else if (changeCount > 0)
                {
                    messageFinal = $"Synchro réussie : {changeCount} éléments synchronisés.";

                    // Mise à jour de l'UI pour la date
                    DateTime now = DateTime.Now;
                    _cloudService.UpdateLocalLastSync(now);
                    TxtLastSync.Text = "Sync: " + now.ToString("g");
                }
                else
                {
                    messageFinal = "Tout est déjà à jour.";
                }

                // 4. Afficher la notification (0.5 pour la durée ou l'opacité selon ton helper)
                await ShowNotification(messageFinal, isError, true, 0.5);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur Sync : " + ex.Message);
                await ShowNotification("Erreur imprévue : " + ex.Message, true, true, 0.5);
            }
            finally
            {
                // 5. Arrêt propre de l'animation et réactivation
                if (rotateTransform != null)
                    rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);

                btn.IsEnabled = true;
                rechargeTotal();
            }
        }
        private string FormatTimeAgo(TimeSpan diff)
        {
            if (diff.TotalMinutes < 1) return "À l'instant";
            if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours} h";
            return $"Le {diff.ToString(@"dd/MM/yyyy")}";
        }
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // On efface le fichier comme dans Settings
            if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath);

            _cloudService.Logout();
            LoadUserProfile(); // Va basculer sur le panneau "Se connecter"
            SettingsViewControl._isLoggedIn = false;
            SettingsViewControl.ShowAppPanel();
        }
        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            BtnSettings_Click(null, new RoutedEventArgs());
            SettingsViewControl.ShowAccountPanel();
        }
        public void rechargeTotal()
        {
            vm = new MainViewModel();
            this.DataContext = vm;
        }
        //actions graph
        private void PrevWeek_Click(object sender, RoutedEventArgs e)
        {
            ((MainViewModel)this.DataContext).ChangeWeek(-7);
        }

        private void NextWeek_Click(object sender, RoutedEventArgs e)
        {
            ((MainViewModel)this.DataContext).ChangeWeek(7);
        }
        private void OpenData_Click(object sender, RoutedEventArgs e)
        {
            HabitStorage.OpenDataFolder();
        }
        private void AddProgress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Habit habit)
            {
                // AddProgress s'occupe déjà de sauvegarder ET de rafraîchir le graphique/listes
                vm.AddProgress(habit, 1);
                // Supprime vm.RefreshData() ici, car AddProgress le gère déjà mieux.
            }

        }
        private void ResetProgress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Habit habit)
            {
                var vm = (MainViewModel)this.DataContext;
                // On remet la valeur à 0
                double current = habit.CurrentValue;
                habit.CurrentValue = 0;

                // On sauvegarde le retrait dans les logs
                HabitStorage.SaveWeeklyProgress(habit.Id, -current);

                vm.RefreshData();
            }
        }
        //actions graph




        //actions de fenetre
        // Pour les paramètres
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            DashboardView.Visibility = Visibility.Collapsed;
            DetailViewControl.Visibility = Visibility.Collapsed;
            SettingsViewControl.Visibility = Visibility.Visible;

            // Style visuel du bouton
            BtnSettings.Foreground = Brushes.Cyan;
            BtnDashboard.Foreground = Brushes.Gray;
        }

        // Pour revenir au Dashboard
        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            SettingsViewControl.Visibility = Visibility.Collapsed;
            DetailViewControl.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;
            LoadUserProfile();
            BtnDashboard.Foreground = Brushes.Cyan;
            BtnSettings.Foreground = Brushes.Gray;
        }

        // Pour le détail (clic sur une carte)
        private void HabitCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border cardBorder && cardBorder.DataContext is Habit selectedHabit)
            {
                // --- CAS 1 : DOUBLE CLIC (Ouverture de l'onglet détail) ---
                if (e.ClickCount == 2)
                {
                    // 1. Charger les données dans la vue
                    DetailViewControl.LoadHabit(selectedHabit);

                    // 2. Logique d'onglets : On cache le Dashboard, on montre le Détail
                    DashboardView.Visibility = Visibility.Collapsed;
                    SettingsViewControl.Visibility = Visibility.Collapsed;
                    DetailViewControl.Visibility = Visibility.Visible;

                    // 3. Tes animations (on les garde, elles s'appliqueront à la vue pleine page)
                    DoubleAnimation slideIn = new DoubleAnimation
                    {
                        From = 0.9,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.2),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    ScaleTransform scale = new ScaleTransform();
                    DetailViewControl.RenderTransform = scale;
                    DetailViewControl.RenderTransformOrigin = new Point(0.5, 0.5);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, slideIn);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, slideIn);
                }
                // --- CAS 2 : CLIC SIMPLE (Focus sur le graphique du Dashboard) ---
                else if (e.ClickCount == 1)
                {
                    // On s'assure que si on était sur un autre onglet, on revient au dashboard
                    if (DashboardView.Visibility != Visibility.Visible)
                    {
                        SwitchToDashboard(); // Une petite méthode helper pour nettoyer l'affichage
                    }

                    // Ton code de filtrage original
                    vm.UpdateChart(selectedHabit.Id);
                }
            }
        }

        // Helper pour revenir au dashboard proprement
        public void SwitchToDashboard()
        {
            DetailViewControl.Visibility = Visibility.Collapsed;
            SettingsViewControl.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;

            // Reset des couleurs de boutons sidebar
            BtnDashboard.Foreground = Brushes.Cyan;
            BtnSettings.Foreground = Brushes.Gray;
        }
        private void ResetChart_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm.UpdateChart(null); // Réaffiche toutes les habitudes
        }
       
        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            OverlayContainer.Visibility = Visibility.Collapsed;
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Pour AddHabitWindow
                          // Application.Current.Shutdown(); // Pour MainWindow si tu veux tout couper
        }

        private void MinimizeApp_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        private void MaximizeApp_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
            }
            else
            {
                this.WindowState = WindowState.Normal;
            }
        }
        //actions de fenetre
    }
}
