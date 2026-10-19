using System.Threading;
using System.Windows;

namespace MangaMeYeah;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Global\MangaMeYeah_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isOnlyInstance);

        if (!isOnlyInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // Ignore mutex release errors on abrupt close
        }
        _singleInstanceMutex = null;

        base.OnExit(e);
        Environment.Exit(0);
    }
}
