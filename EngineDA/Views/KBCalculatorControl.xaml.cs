using System;
using System.Windows;
using System.Windows.Input;

namespace EngineDA.Views
{
    public partial class KBCalculatorWindow : Window
    {
        public ConfirmDialog? confirmDialog { get; set; }

        public KBCalculatorWindow()
        {
            InitializeComponent();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtXMin.Text, out double xMin) &&
                double.TryParse(txtXMax.Text, out double xMax) &&
                double.TryParse(txtYMin.Text, out double yMin) &&
                double.TryParse(txtYMax.Text, out double yMax))
            {
                if (Math.Abs(xMax - xMin) < 1e-6)
                {
                    confirmDialog = new Views.ConfirmDialog("通道量程的最大值不能等于最小值!");
                    confirmDialog.ShowDialog();
                    confirmDialog = null;
                    return;
                }


                double k = (yMax - yMin) / (xMax - xMin);
                double b = yMin - k * xMin;

                txtK.Text = k.ToString("G17");
                txtB.Text = b.ToString("G17");
            }
            else
            {
                confirmDialog = new Views.ConfirmDialog("请输入有效的数字!");
                confirmDialog.ShowDialog();
                confirmDialog = null;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}