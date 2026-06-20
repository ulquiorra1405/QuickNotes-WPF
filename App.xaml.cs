using System.Windows;
using System.Windows.Threading;
using QuickNotes.Models;

namespace QuickNotes;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
            ShowFatalError(args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorLog.Write(ex, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void ShowFatalError(Exception ex)
    {
        var msg = $"Ocurrió un error inesperado.\n\n{ex.GetType().Name}: {ex.Message}\n\nSe ha guardado un registro en Documentos/QuickNotes/error.log";
        if (Current.MainWindow != null)
            MessageBox.Show(Current.MainWindow, msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

