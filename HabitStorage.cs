using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FxHabit
{
    public static class HabitStorage
    {
        // On définit le nom du dossier et le chemin complet
        private static readonly string FolderName = "Data";
        private static readonly string FileName = "habits.json";
        private static readonly string StatsFolderName = "HabitStats";

        public static string GetFilePath()
        {
            // Création du chemin vers le dossier Data à la racine de l'appli
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName);

            // Si le dossier n'existe pas, on le crée
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return Path.Combine(folderPath, FileName);
        }
        private static string GetHabitStatsPath(Guid habitId)
        {
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", StatsFolderName);
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            return Path.Combine(folderPath, $"{habitId}.json");
        }
        // Sauvegarder TOUTE la liste (Utile pour les réorganisations ou suppressions)
        public static void SaveAllHabits(IEnumerable<Habit> habits)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(habits, options);
                File.WriteAllText(GetFilePath(), jsonString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur SaveAll: {ex.Message}");
            }
        }

        // SAUVEGARDER UNE SEULE HABITUDE (Ajout ou Mise à jour)
        public static void SaveSingleHabit(Habit habit)
        {
            // 1. Charger la liste existante
            var currentHabits = LoadHabits().ToList();

            // 2. Vérifier si elle existe déjà (Update) ou si c'est une nouvelle (Add)
            var existingIndex = currentHabits.FindIndex(h => h.Id == habit.Id);

            if (existingIndex != -1)
            {
                currentHabits[existingIndex] = habit; // Mise à jour
            }
            else
            {
                currentHabits.Add(habit); // Nouvel ajout
            }

            // 3. Réenregistrer le tout
            SaveAllHabits(currentHabits);
        }

        // Charger les habitudes
        public static ObservableCollection<Habit> LoadHabits()
        {
            string path = GetFilePath();

            if (!File.Exists(path))
                return new ObservableCollection<Habit>();

            try
            {
                string jsonString = File.ReadAllText(path);
                var habits = JsonSerializer.Deserialize<List<Habit>>(jsonString);
                return habits != null
                    ? new ObservableCollection<Habit>(habits)
                    : new ObservableCollection<Habit>();
            }
            catch
            {
                return new ObservableCollection<Habit>();
            }
        }



        // Enregistre une action pour n'importe quelle habitude dans le fichier de la semaine
        public static void SaveWeeklyProgress(Guid habitId, double value)
        {
            DateTime now = DateTime.Now;

            // 1. Sauvegarde dans le fichier de la SEMAINE (pour le Dashboard global)
            string weeklyPath = GetWeeklyPath(now);
            UpdateLogFile(weeklyPath, habitId, now, value);

            // 2. Sauvegarde dans le fichier DÉDIÉ à l'habitude (pour les stats de l'habitude)
            string habitPath = GetHabitStatsPath(habitId);
            UpdateLogFile(habitPath, habitId, now, value);
        }
        public static void OpenDataFolder()
        {
            try
            {
                // On récupère le chemin du dossier Data
                string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

                // Si pour une raison quelconque le dossier n'existe pas encore, on le crée
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // On lance l'explorateur Windows sur ce dossier
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Impossible d'ouvrir le dossier : " + ex.Message);
            }
        }
        private static void UpdateLogFile(string path, Guid habitId, DateTime date, double value)
        {
            List<HabitLog> logs = new List<HabitLog>();

            // 1. Charger l'existant s'il existe
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    logs = JsonSerializer.Deserialize<List<HabitLog>>(json) ?? new List<HabitLog>();
                }
                catch { logs = new List<HabitLog>(); }
            }

            // 2. Chercher si un log existe déjà pour CETTE habitude et CE JOUR précise
            var existingLog = logs.FirstOrDefault(l => l.HabitId == habitId && l.Date.Date == date.Date);

            if (existingLog != null)
            {
                // On ajoute la valeur à l'existant (ex: +1)
                existingLog.ProgressValue += value;
            }
            else
            {
                // On crée une nouvelle entrée pour cette habitude
                logs.Add(new HabitLog
                {
                    HabitId = habitId,
                    Date = date.Date,
                    ProgressValue = value
                });
            }

            // 3. Sauvegarder
            string updatedJson = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, updatedJson);
        }

        public static string GetWeeklyPath(DateTime date)
        {
            var dfi = System.Globalization.DateTimeFormatInfo.CurrentInfo;
            var cal = dfi.Calendar;
            // On récupère le numéro de semaine ISO
            int weekNum = cal.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            int year = date.Year;

            // Cas particulier : une semaine de fin décembre peut appartenir à l'année suivante
            if (weekNum >= 52 && date.Month == 1) year--;

            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Logs");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            return Path.Combine(folderPath, $"week_{weekNum}_{year}.json");
        }
        public static List<HabitLog> LoadHabitStats(Guid habitId)
        {
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "HabitStats");
            string filePath = Path.Combine(folderPath, $"{habitId}.json");

            if (!File.Exists(filePath)) return new List<HabitLog>();

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<HabitLog>>(json) ?? new List<HabitLog>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur chargement stats dédiées: {ex.Message}");
                return new List<HabitLog>();
            }
        }
        public static List<HabitLog> LoadWeeklyLogs(DateTime date)
        {
            string path = GetWeeklyPath(date);
            if (!File.Exists(path)) return new List<HabitLog>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<HabitLog>>(json) ?? new List<HabitLog>();
            }
            catch { return new List<HabitLog>(); }
        }
        // Supprimer une habitude
        public static void DeleteHabit(Guid habitId)
        {
            // 1. Supprimer l'habitude de la liste principale (habits.json)
            var habits = LoadHabits();
            var toRemove = habits.FirstOrDefault(h => h.Id == habitId);
            if (toRemove != null)
            {
                habits.Remove(toRemove);
                SaveAllHabits(habits);
            }

            // 2. Supprimer le fichier de statistiques DÉDIÉ
            try
            {
                string habitStatsPath = GetHabitStatsPath(habitId);
                if (File.Exists(habitStatsPath))
                {
                    File.Delete(habitStatsPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur suppression stats: {ex.Message}");
            }

            // Note : On ne touche pas au dossier "Logs" (weekly) pour garder l'historique global
        }
    }
    public class HabitLog { public Guid HabitId { get; set; } public DateTime Date { get; set; } public double ProgressValue { get; set; } }
}