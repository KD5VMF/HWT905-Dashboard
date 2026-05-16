using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HWT905Dashboard;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.Message, "HWT905 Dashboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("UnhandledException", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWT905Dashboard");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "crash_log.txt");
            File.AppendAllText(path,
                $"\n===== {DateTime.Now:O} {source} =====\n{ex}\n");
        }
        catch
        {
            // Last-chance logger must never throw.
        }
    }
}
