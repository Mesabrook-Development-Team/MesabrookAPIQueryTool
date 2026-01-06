using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MesabrookAPIQueryTool
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            DataContext = new LoginWindowViewModel(closeFailAction: () =>
            {
                DialogResult = false;
                Close();
            }, closeSucceedAction: () =>
            {
                DialogResult = true;
                Close();
            });
        }
    }
}
