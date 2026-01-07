using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace FxHabit
{
    public partial class AddHabitWindow : Window
    {
        public Habit NewHabit { get; set; }
        public ObservableCollection<string> Reminders { get; set; } = new ObservableCollection<string>();

        public AddHabitWindow()
        {
            InitializeComponent();
            ListReminders.ItemsSource = Reminders;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtNewReminder.Text))
            {
                // Simple validation de format HH:mm
                if (System.Text.RegularExpressions.Regex.IsMatch(TxtNewReminder.Text, @"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$"))
                {
                    if (!Reminders.Contains(TxtNewReminder.Text))
                    {
                        Reminders.Add(TxtNewReminder.Text);
                        TxtNewReminder.Clear();
                    }
                }
                else
                {
                    MessageBox.Show("Format invalide. Utilisez HH:mm");
                }
            }
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var time = btn.DataContext as string;
            Reminders.Remove(time);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Validation minimum
            if (string.IsNullOrWhiteSpace(TxtTitle.Text))
            {
                MessageBox.Show("Veuillez donner un nom à la mission.");
                return;
            }

            // Récupérer l'icône
            string icon = "Star";
            if (IconSelector.SelectedItem is PackIcon selectedIcon)
                icon = selectedIcon.Kind.ToString();

            // Récupérer la couleur
            string color = "#00FFFF";
            if (ColorPurple.IsChecked == true) color = "#BC13FE";
            else if (ColorOrange.IsChecked == true) color = "#FF9D00";
            else if (ColorGreen.IsChecked == true) color = "Lime";
            else if (ColorRed.IsChecked == true) color = "#FF4545";
            else if (ColorPink.IsChecked == true) color = "#FF007F";
            else if (ColorYellow.IsChecked == true) color = "#FFFF00";
            else if (ColorWhite.IsChecked == true) color = "#FFF9F7";
            else if (ColorBlack.IsChecked == true) color = "#000000";
            else if (ColorDarkBlue.IsChecked == true) color = "#FF0101";
            else if (ColorDarkGreen.IsChecked == true) color = "#FF0578";

            NewHabit = new Habit
            {
                Id = Guid.NewGuid(),
                Title = TxtTitle.Text,
                IconKey = icon,
                ColorHex = color,
                DailyGoal =SliderGoal.Value,
                Unit = TxtUnit.Text,
                ReminderTimes = Reminders.ToList(),
                TargetDays = GetSelectedDays(),
                CreatedAt = DateTime.Now,
                IsReminderEnabled = Reminders.Any()
            };

            this.DialogResult = true;
            this.Close();
        }

        private List<int> GetSelectedDays()
        {
            var days = new List<int>();
            if (Day1.IsChecked == true) days.Add(1);
            if (Day2.IsChecked == true) days.Add(2);
            if (Day3.IsChecked == true) days.Add(3);
            if (Day4.IsChecked == true) days.Add(4);
            if (Day5.IsChecked == true) days.Add(5);
            if (Day6.IsChecked == true) days.Add(6);
            if (Day7.IsChecked == true) days.Add(7);
            return days;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}