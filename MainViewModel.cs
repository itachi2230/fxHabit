    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using LiveCharts;
    using LiveCharts.Wpf;
    using System.Windows.Media;

    namespace FxHabit
    {
        public class MainViewModel : BindableBase
        {
            private ObservableCollection<Habit> _habitList;
            private DateTime _currentWeekDate = DateTime.Today;
            private SeriesCollection _chartSeries;
            private List<string> _labels;
        // Séparation des listes pour le design
        public IEnumerable<Habit> PendingHabits => HabitList.Where(h => !IsCompletedToday(h));
            public IEnumerable<Habit> CompletedHabits => HabitList.Where(h => IsCompletedToday(h));

            public ObservableCollection<Habit> HabitList
            {
                get => _habitList;
                set { SetProperty(ref _habitList, value); OnPropertyChanged(nameof(HabitsToday)); }
            }
            public double GetTodayProgress(Guid habitId)
            {
                var logs = HabitStorage.LoadWeeklyLogs(DateTime.Today);
                return logs.Where(l => l.HabitId == habitId && l.Date.Date == DateTime.Today)
                           .Sum(l => l.ProgressValue);
            }
            public SeriesCollection ChartSeries
            {
                get => _chartSeries;
                set => SetProperty(ref _chartSeries, value);
            }

            public List<string> Labels
            {
                get => _labels;
                set => SetProperty(ref _labels, value);
            }

            public string WeekRangeDisplay => GetWeekRangeText(_currentWeekDate);

            public MainViewModel()
            {
                HabitList = HabitStorage.LoadHabits() ?? new ObservableCollection<Habit>();
                Labels = new List<string> { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };
                RefreshData();
            }

            public void ChangeWeek(int deltaDays)
            {
                _currentWeekDate = _currentWeekDate.AddDays(deltaDays);
                RefreshData();
            }

            public void RefreshData()
            {
                var logs = HabitStorage.LoadWeeklyLogs(DateTime.Today);

                foreach (var habit in HabitList)
                {
                    // On récupère la somme pour l'ID spécifique
                    double totalToday = logs.Where(l => l.HabitId == habit.Id && l.Date.Date == DateTime.Today)
                                            .Sum(l => l.ProgressValue);

                    habit.CurrentValue = totalToday;
                    
                }

                // Force la vue à recalculer les listes filtrées
                OnPropertyChanged(nameof(PendingHabits));
                OnPropertyChanged(nameof(CompletedHabits));

                UpdateChart();
                OnPropertyChanged(nameof(WeekRangeDisplay));
            }

             public IEnumerable<Habit> HabitsToday => HabitList.Where(h => h.TargetDays.Contains((int)(DateTime.Now.DayOfWeek == 0 ? 7 : (int)DateTime.Now.DayOfWeek)));

        private Guid? _focusedHabitId = null; // Variable pour garder en mémoire le filtre actuel

        public void UpdateChart(Guid? filterHabitId = null)
        {
            // On met à jour l'ID filtré. 
            // Si on clique sur une carte déjà filtrée, on pourrait aussi imaginer reset (optionnel)
            _focusedHabitId = filterHabitId;

            DateTime startOfWeek = _currentWeekDate.AddDays(-(int)(_currentWeekDate.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)_currentWeekDate.DayOfWeek - 1)).Date;
            var weeklyLogs = HabitStorage.LoadWeeklyLogs(startOfWeek);

            var newSeries = new SeriesCollection();

            // FILTRAGE : Si _focusedHabitId a une valeur, on ne prend que cette habitude.
            // Sinon, on prend toute la liste HabitList.
            var habitsToShow = _focusedHabitId.HasValue
                ? HabitList.Where(h => h.Id == _focusedHabitId.Value)
                : HabitList;

            foreach (var habit in habitsToShow)
            {
                var values = new ChartValues<double>();
                for (int i = 0; i < 7; i++)
                {
                    DateTime targetDate = startOfWeek.AddDays(i);
                    var log = weeklyLogs.FirstOrDefault(l => l.HabitId == habit.Id && l.Date.Date == targetDate.Date);

                    // Calcul du % basé sur DailyGoal
                    double progress = 0;
                    if (log != null && habit.DailyGoal > 0)
                    {
                        progress = Math.Min(100, (log.ProgressValue / habit.DailyGoal) * 100);
                    }
                    values.Add(progress);
                }

                newSeries.Add(new LineSeries
                {
                    Title = habit.Title,
                    Values = values,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(habit.ColorHex)),
                    PointForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(habit.ColorHex)),
                    Fill = Brushes.Transparent,
                    PointGeometrySize = 10,
                    PointGeometry = DefaultGeometries.Circle,
                    LineSmoothness = 0.5
                });
            }

            ChartSeries = newSeries;
        }
        // Vérifie si l'objectif est atteint pour aujourd'hui
        public void AddProgress(Habit habit, double amount)
            {
                // 1. On modifie la propriété (ce qui déclenche le OnPropertyChanged de IsCompleted)
                habit.CurrentValue += amount;

                // 2. On sauvegarde l'ID spécifique (très important pour ne pas mélanger les habitudes)
                HabitStorage.SaveWeeklyProgress(habit.Id, amount);

                // 3. On rafraîchit les vues pour que la carte "saute" d'une liste à l'autre
                OnPropertyChanged(nameof(PendingHabits));
                OnPropertyChanged(nameof(CompletedHabits));

                UpdateChart();
            }
            private bool IsCompletedToday(Habit h)
            {
                // On charge les logs de la semaine courante
                var logs = HabitStorage.LoadWeeklyLogs(DateTime.Today);

                // On somme tout ce qui a été fait AUJOURD'HUI pour cet ID
                double totalDone = logs.Where(l => l.HabitId == h.Id && l.Date.Date == DateTime.Today)
                                       .Sum(l => l.ProgressValue);

                // Important : On compare à l'objectif
                return totalDone >= h.DailyGoal;
            }
       
            private string GetWeekRangeText(DateTime dt)
            {
                DateTime start = dt.AddDays(-(int)(dt.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)dt.DayOfWeek - 1));
                return $"{start:dd MMM} - {start.AddDays(6):dd MMM} {start.Year}".ToUpper();
            }
        }
    }