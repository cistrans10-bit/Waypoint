using System.Windows;
using System.Windows.Threading;
using Waypoint.Services;
using Waypoint.ViewModels;

namespace Waypoint;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => SettingsService.LogError(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { SettingsService.LogError(args.Exception); args.SetObserved(); };

        ThemeService.Initialize();
        var settings = SettingsService.Load();
        var window = new MainWindow(new MainViewModel(settings, e.Args.FirstOrDefault()));
        MainWindow = window;
        window.Show();
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        SettingsService.LogError(e.Exception);
        MessageBox.Show(e.Exception.Message, "Waypoint", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
