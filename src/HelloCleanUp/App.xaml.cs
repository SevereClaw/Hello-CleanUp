using System.Windows;
using System.Windows.Threading;

namespace HelloCleanUp
{
    public partial class App : Application
    {
        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Что-то пошло не так:\n\n" + e.Exception.Message, "Hello, CleanUp.",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
