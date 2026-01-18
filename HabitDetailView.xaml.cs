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
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ReturnToHome();
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
            // 1. Chargement via le Storage (Stratégie centralisée)
            var logs = HabitStorage.LoadHabitStats(habitId)
                                   .OrderBy(l => l.Date)
                                   .ToList();

            if (logs.Count == 0)
            {
                SetEmptyState();
                return;
            }

            // --- 1. CALCULS DES WIDGETS DE BASE ---
            double total = logs.Sum(l => l.ProgressValue);
            TotalValueDisplay = $"{total} {_currentHabit.Unit}";

            int daysSuccess = logs.Count(l => l.ProgressValue >= _currentHabit.DailyGoal);
            double rate = (daysSuccess / (double)logs.Count) * 100;
            CompletionRate = $"{Math.Round(rate)}%";
            CompletionPercentage = rate;

            // --- 2. CALCULS AVANCÉS (INSIGHTS) ---
            _currentHabit.Streak = CalculateCurrentStreak(logs, _currentHabit.DailyGoal);
            StreakDisplay = $"{_currentHabit.Streak} Jours";
            MaxStreak = $"{CalculateMaxStreak(logs, _currentHabit.DailyGoal)} Jours";

            // Meilleur jour
            string[] frenchDays = { "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };
            var bestDayGroup = logs.GroupBy(l => l.Date.DayOfWeek)
                                   .OrderByDescending(g => g.Average(x => x.ProgressValue))
                                   .FirstOrDefault();
            BestDay = bestDayGroup != null ? frenchDays[(int)bestDayGroup.Key] : "N/A";

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

            // Récupération sécurisée de la couleur
            Color baseColor;
            try
            {
                baseColor = (Color)ColorConverter.ConvertFromString(_currentHabit.ColorHex ?? "Cyan");
            }
            catch
            {
                baseColor = Colors.Cyan;
            }

            MonthlySeries = new SeriesCollection {
        new LineSeries {
            Values = monthlyValues,
            Stroke = new SolidColorBrush(baseColor),
            Fill = new LinearGradientBrush(Color.FromArgb(40, baseColor.R, baseColor.G, baseColor.B), Colors.Transparent, 90),
            PointGeometrySize = 0,
            LineSmoothness = 0.6 // Un peu moins que 1 pour un look plus "technique"
        }
    };

            // --- 4. DISTRIBUTION HEBDOMADAIRE ---
            var dayAverages = new double[7];
            // On commence par Lundi (index 1 dans DayOfWeek) jusqu'à Dimanche (index 0)
            DayOfWeek[] weekOrder = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

            for (int i = 0; i < 7; i++)
            {
                var day = weekOrder[i];
                var dayLogs = logs.Where(l => l.Date.DayOfWeek == day).ToList();
                dayAverages[i] = dayLogs.Count > 0 ? dayLogs.Average(l => l.ProgressValue) : 0;
            }

            WeeklyDistributionSeries = new SeriesCollection {
        new ColumnSeries {
            Values = new ChartValues<double>(dayAverages),
            Fill = new SolidColorBrush(baseColor),
            MaxColumnWidth = 15
        }
    };
        }

        // Pour éviter les bugs si l'habitude est toute neuve sans logs
        private void SetEmptyState()
        {
            TotalValueDisplay = $"0 {_currentHabit.Unit}";
            CompletionRate = "0%";
            StreakDisplay = "0 Jours";
            MaxStreak = "0 Jours";
            BestDay = "N/A";
            GoalDisplay = $"{_currentHabit.DailyGoal} {_currentHabit.Unit} / jour";
            CreatedAtDisplay = "Aucune donnée enregistrée";
            MonthlySeries = new SeriesCollection();
            WeeklyDistributionSeries = new SeriesCollection();
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
       
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            // Logique de modification
        }
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = CyberMessageBox.Show(
            "Voulez-vous vraiment supprimer définitivement cette mission ?",
            "ALERTE SUPPRESSION",
            true);

            if (confirm)
            {
                var mainWindow = Window.GetWindow(this) as MainWindow;
                var vm = mainWindow?.DataContext as MainViewModel;

                if (vm != null && _currentHabit != null)
                {
                    // 1. Supprimer de la liste Observable (Maj interface)
                    vm.HabitList.Remove(_currentHabit);
                    HabitStorage.DeleteHabit(_currentHabit.Id);
                    vm.RefreshData();
                    ReturnToHome();
                }
            }
        }
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        private void ReturnToHome()
        {
            // On cherche la MainWindow (le parent de l'application)
            var mainWindow = Window.GetWindow(this) as MainWindow;

            if (mainWindow != null)
            {
                // On appelle la méthode de la MainWindow qui affiche la liste des habitudes
                // Si ta méthode s'appelle 'ShowHabitsList' ou 'NavigateToDashboard' :
                mainWindow.SwitchToDashboard();
            }
        }
    }
}