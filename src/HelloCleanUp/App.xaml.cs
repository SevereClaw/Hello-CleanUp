using System.Windows;
using System.Windows.Threading;

namespace HelloCleanUp
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Any argument switches to the command-line mode: no window, exit code for scripts.
            if (e.Args.Length > 0)
            {
                Shutdown(Cli.Run(e.Args));
                return;
            }
            new MainWindow().Show();
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Что-то пошло не так:\n\n" + e.Exception.Message, "Hello, CleanUp.",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
