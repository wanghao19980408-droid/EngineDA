using System.Windows;
using System.Windows.Input;

namespace EngineDA.Views
{
    public partial class PasswordDialog : Window
    {
        public bool IsAuthenticated { get; private set; } = false;

        public PasswordDialog()
        {
            InitializeComponent();
            pwdBox.Focus();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (pwdBox.Password == "123456")
            {
                IsAuthenticated = true;
                Close();
            }
            else
            {
                txtError.Visibility = Visibility.Visible;
                pwdBox.Clear();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}