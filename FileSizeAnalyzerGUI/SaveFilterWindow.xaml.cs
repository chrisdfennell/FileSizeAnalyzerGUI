using System.Windows;

namespace FileSizeAnalyzerGUI
{
    public partial class SaveFilterWindow : Window
    {
        public string FilterName { get; private set; }

        public SaveFilterWindow()
        {
            InitializeComponent();
            FilterNameTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilterNameTextBox.Text))
            {
                MessageBox.Show("Please enter a name for the filter.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            FilterName = FilterNameTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
