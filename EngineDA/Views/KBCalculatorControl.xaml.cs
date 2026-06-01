using System;
using System.Windows;

namespace EngineDA.Views
{
    public partial class KBCalculatorWindow : Window
    {

        public ConfirmDialog? confirmDialog { get; set; }
        public KBCalculatorWindow()
        {
            InitializeComponent();
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
                    confirmDialog = new Views.ConfirmDialog("采集最大值不能等于最小值!");
                    confirmDialog.ShowDialog();
                    confirmDialog = null;
                    return;
                }

                double k = (yMax - yMin) / (xMax - xMin);
                double b = yMin - k * xMin;

                txtK.Text = k.ToString("F6");
                txtB.Text = b.ToString("F6");
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
