using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FxHabit
{
    public class Habit : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; }
        public string IconKey { get; set; }
        public string ColorHex { get; set; }

        public List<int> TargetDays { get; set; } = new List<int>();
        public List<string> ReminderTimes { get; set; } = new List<string>();

        private int _streak;
        public int Streak
        {
            get => _streak;
            set { _streak = value; OnPropertyChanged(); }
        }

        public double DailyGoal { get; set; }

        private double _currentValue;
        [Newtonsoft.Json.JsonIgnore]
        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                _currentValue = value;
                OnPropertyChanged();
                // Mise à jour automatique de l'état de complétion
                OnPropertyChanged(nameof(IsCompleted));
            }
        }

        // PROPRIÉTÉ CALCULÉE : C'est elle qui décide dans quelle liste va la carte
        [Newtonsoft.Json.JsonIgnore]
        public bool IsCompleted => CurrentValue >= DailyGoal;

        public string Unit { get; set; }
        public Dictionary<DateTime, double> History { get; set; } = new Dictionary<DateTime, double>();
        public bool IsReminderEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
public class HeatmapPoint
    {
        public DateTime Date { get; set; }
        public double Intensity { get; set; } // De 0.1 à 1.0 pour l'opacité
        public string Color { get; set; }
    }
}