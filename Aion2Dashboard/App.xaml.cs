using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Aion2Dashboard;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory,
        "logs",
        $"crash-{DateTime.Now:yyyyMMdd}.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"예기치 않은 오류가 발생했습니다.\n\n로그: {LogPath}\n\n{e.Exception.Message}",
            "DPSVIEWER 오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("UnhandledException", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}");
            builder.AppendLine(exception.ToString());
            builder.AppendLine(new string('-', 80));
            File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }
}
