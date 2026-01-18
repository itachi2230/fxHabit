using System.Windows;

namespace FxHabit
{
    public partial class CyberMessageBox : Window
    {
        public bool Result { get; set; }

        public CyberMessageBox(string message, string title = "CONFIRMATION SYSTÈME", bool isWarning = true)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;
            IconAlert.Foreground = isWarning ?
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 68, 68)) :
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 255));
        }

        public static bool Show(string message, string title = "CONFIRMATION SYSTÈME", bool isWarning = true)
        {
            var msg = new CyberMessageBox(message, title, isWarning);
            msg.ShowDialog();
            return msg.Result;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.Close();
        }
    }
}