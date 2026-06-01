using System.Windows;
using System.Windows.Input;

namespace EngineDA.Views
{
    public partial class ConfirmDialog : Window
    {
        public bool Result { get; private set; } = false;

        public ConfirmDialog(string message)
        {
            InitializeComponent();
            MessageTextBlock.Text = message;
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}