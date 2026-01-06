using System.Windows;

namespace MesabrookAPIQueryTool
{
    /// <summary>
    /// Interaction logic for RegisterWindow.xaml
    /// </summary>
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
            DataContext = new RegisterWindowViewModel(Close);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Topmost = false;
        }
    }
}
