using System.Configuration;
using System.Data;
using System.Windows;
using RGZ_TIMP.Views;
using RGZ_TIMP.ViewModels;

namespace RGZ_TIMP
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            mainWindow.Show();
        }
    }
}
