using System.Windows;
using MysteriousCharacters.App.Services;

namespace MysteriousCharacters.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\MysteriousCharacters", out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "隐文匣已经在后台运行，请从系统托盘打开设置。",
                "隐文匣",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        var dictionaryService = new DictionaryService();
        var window = new MainWindow(settingsService, dictionaryService);

        MainWindow = window;
        window.InitializeRuntime();
        window.OpenSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
