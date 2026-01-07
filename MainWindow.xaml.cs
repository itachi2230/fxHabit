using LiveCharts;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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
        public MainWindow()
        {
            InitializeComponent();
            
            this.DataContext = vm;
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
        private void HabitCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                if (sender is Border cardBorder && cardBorder.DataContext is Habit selectedHabit)
                {
                    // 1. Charger les données
                    DetailView.LoadHabit(selectedHabit);

                    // 2. Rendre visible SANS animation d'abord pour tester
                    OverlayContainer.Opacity = 1;
                    OverlayContainer.Visibility = Visibility.Visible;

                    // 3. Optionnel : Animation de "Pop Up" légère
                    DoubleAnimation slideIn = new DoubleAnimation
                    {
                        From = 0.9,
                        To = 1.0,
                        Duration = TimeSpan.FromSeconds(0.2)
                    };
                    ScaleTransform scale = new ScaleTransform();
                    DetailView.RenderTransform = scale;
                    DetailView.RenderTransformOrigin = new Point(0.5, 0.5);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, slideIn);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, slideIn);
                }
            }
        }
        private void Overlay_OutsideClick(object sender, MouseButtonEventArgs e)
        {
            OverlayContainer.Visibility = Visibility.Collapsed;
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
