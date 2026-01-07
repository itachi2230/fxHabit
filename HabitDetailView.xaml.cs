using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using LiveCharts;
using LiveCharts.Wpf;
using System.Windows.Media;

namespace FxHabit
{
    public partial class HabitDetailView : UserControl, INotifyPropertyChanged
    {
        private Habit _currentHabit;

        // Propriétés pour le Binding
        public double CompletionPercentage { get; set; } // Pour la ProgressBar
        public string MaxStreak { get; set; }
        public string BestDay { get; set; }
        public string GoalDisplay { get; set; }
        public string CreatedAtDisplay { get; set; }
        public string RemindersDisplay => "Activés (08:00)"; // Exemple statique ou à lier
        public string TargetDaysDisplay => "Lun, Mar, Mer, Jeu, Ven, Sam, Dim"; // À dynamiser selon ton modèle
        public Brush CategoryBrush => new SolidColorBrush(Color.FromArgb(100, 0, 255, 255));
        public string Title => _currentHabit?.Title ?? "---";
        public string StreakDisplay { get; set; }
        public string CompletionRate { get; set; }
        public string TotalValueDisplay { get; set; }
        public SeriesCollection MonthlySeries { get; set; }
        public SeriesCollection WeeklyDistributionSeries { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public HabitDetailView()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        public void LoadHabit(Habit habit)
        {
            _currentHabit = habit;
            CalculateStats(habit.Id);

            // On prévient l'UI que tout a changé
            OnPropertyChanged(string.Empty);
        }
        private int CalculateMaxStreak(List<HabitLog> logs, double goal)
        {
            int maxStreak = 0;
            int currentCounter = 0;

            // On trie par date pour parcourir l'historique
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();

            foreach (var log in sortedLogs)
            {
                if (log.ProgressValue >= goal)
                {
                    currentCounter++;
                    if (currentCounter > maxStreak) maxStreak = currentCounter;
                }
                else
                {
                    currentCounter = 0; // La série est brisée
                }
            }
            return maxStreak;
        }
        private void CalculateStats(Guid habitId)
        {
            string statsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HabitStats", $"{habitId}.json");
            if (!File.Exists(statsPath)) return;

            var logs = JsonSerializer.Deserialize<List<HabitLog>>(File.ReadAllText(statsPath))
                       .OrderBy(l => l.Date).ToList() ?? new List<HabitLog>();

            // --- 1. CALCULS DES WIDGETS DE BASE ---
            double total = logs.Sum(l => l.ProgressValue);
            TotalValueDisplay = $"{total} {_currentHabit.Unit}";

            int daysSuccess = logs.Count(l => l.ProgressValue >= _currentHabit.DailyGoal);
            double rate = logs.Count > 0 ? (daysSuccess / (double)logs.Count) * 100 : 0;
            CompletionRate = $"{Math.Round(rate)}%";
            CompletionPercentage = rate; // Pour la ProgressBar circulaire

            // --- 2. CALCULS AVANCÉS (INSIGHTS) ---
            _currentHabit.Streak = CalculateCurrentStreak(logs, _currentHabit.DailyGoal);
            StreakDisplay = $"{_currentHabit.Streak} Jours";
            MaxStreak = $"{CalculateMaxStreak(logs, _currentHabit.DailyGoal)} Jours";

            // Meilleur jour (Basé sur la moyenne de réussite par jour de semaine)
            string[] frenchDays = { "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };
            var bestDayGroup = logs.GroupBy(l => l.Date.DayOfWeek)
                                   .OrderByDescending(g => g.Average(x => x.ProgressValue))
                                   .FirstOrDefault();
            BestDay = bestDayGroup != null ? frenchDays[(int)bestDayGroup.Key] : "N/A";

            // Infos de l'habitude
            GoalDisplay = $"{_currentHabit.DailyGoal} {_currentHabit.Unit} / jour";
            CreatedAtDisplay = $"Suivi depuis le {logs.Min(l => l.Date):dd MMMM yyyy}";

            // --- 3. GRAPHIQUE MENSUEL (30J) ---
            var monthlyValues = new ChartValues<double>();
            for (int i = 29; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var val = logs.FirstOrDefault(l => l.Date.Date == date.Date)?.ProgressValue ?? 0;
                monthlyValues.Add(val);
            }

            Color baseColor = (Color)ColorConverter.ConvertFromString(_currentHabit.ColorHex);
            MonthlySeries = new SeriesCollection {
        new LineSeries {
            Values = monthlyValues,
            Stroke = new SolidColorBrush(baseColor),
            Fill = new LinearGradientBrush(Color.FromArgb(40, baseColor.R, baseColor.G, baseColor.B), Colors.Transparent, 90),
            PointGeometrySize = 0,
            LineSmoothness = 1
        }
    };

            // --- 4. DISTRIBUTION HEBDOMADAIRE (BARRÉS) ---
            var dayAverages = new double[7];
            for (int i = 1; i <= 7; i++)
            {
                var day = (DayOfWeek)(i % 7);
                var dayLogs = logs.Where(l => l.Date.DayOfWeek == day).ToList();
                dayAverages[i - 1] = dayLogs.Count > 0 ? dayLogs.Average(l => l.ProgressValue) : 0;
            }

            WeeklyDistributionSeries = new SeriesCollection {
        new ColumnSeries {
            Values = new ChartValues<double>(dayAverages),
            Fill = new SolidColorBrush(baseColor),
            MaxColumnWidth = 15
        }
    };
        }
        // --- EVENEMENTS BOUTONS ---
        private int CalculateCurrentStreak(List<HabitLog> logs, double goal)
        {
            int streak = 0;
            DateTime checkDate = DateTime.Today;

            // On remonte le temps tant que l'objectif est atteint
            while (true)
            {
                var log = logs.FirstOrDefault(l => l.Date.Date == checkDate.Date);
                if (log != null && log.ProgressValue >= goal)
                {
                    streak++;
                    checkDate = checkDate.AddDays(-1);
                }
                else
                {
                    // Si on ne trouve pas de log pour hier, la série s'arrête
                    break;
                }
            }
            return streak;
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // On remonte l'info à la fenêtre principale pour cacher l'overlay
            var parent = Window.GetWindow(this) as MainWindow;
            if (parent != null) parent.OverlayContainer.Visibility = Visibility.Collapsed;
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            // Logique de modification
        }
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show($"Voulez-vous vraiment supprimer '{_currentHabit.Title}' et tout son historique ?",
                                         "Confirmation de suppression",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var mainWindow = Window.GetWindow(this) as MainWindow;
                var vm = mainWindow?.DataContext as MainViewModel;

                if (vm != null && _currentHabit != null)
                {
                    // 1. Supprimer de la liste Observable (Maj interface)
                    vm.HabitList.Remove(_currentHabit);

                    // 2. Supprimer le fichier de stats dédié
                    string statsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HabitStats", $"{_currentHabit.Id}.json");
                    if (File.Exists(statsPath)) File.Delete(statsPath);

                    // 3. Sauvegarder la liste globale mise à jour (si tu as une fonction SaveAll)
                    // HabitStorage.SaveAllHabits(vm.HabitList); 

                    // 4. Fermer l'overlay et rafraîchir le dashboard
                    mainWindow.OverlayContainer.Visibility = Visibility.Collapsed;
                    vm.RefreshData();
                }
            }
        }
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}