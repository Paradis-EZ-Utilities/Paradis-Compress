using System.Windows;
using System.Windows.Threading;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Services;
using ParadisCompress.UI;

namespace ParadisCompress;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Initialize();
        Log.Info($"Paradis Compress starting (v{AppInfo.Version}) on {Environment.OSVersion.VersionString}, .NET {Environment.Version}");

        // Every ffmpeg/ffprobe we start is tied to this process: if the app is closed or crashes,
        // Windows ends them too, so no orphaned encoder keeps writing a half-finished file.
        FfmpegProcess.ProcessStarted = ChildProcessGuard.Adopt;
        CompressionJob.Log = Log.Info;

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warn("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0) window.LoadFromCommandLine(e.Args[0]);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Paradis Compress exiting");
        Log.Shutdown();
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        Dialogs.Error(MainWindow, "Something went wrong",
            "Paradis Compress hit an unexpected problem. Your original video is untouched.\n\n" +
            $"Technical details are in:\n{Log.Directory}");
    }
}
