using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MangaMeYeah.Models;

namespace MangaMeYeah;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static CancellationTokenSource? _pipeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureLeftAlignedMenuDrop();

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"予期せぬエラーが発生しました:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "MangaMeYeah エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = ViewerSettings.Load();
        if (settings.PreventMultipleInstances)
        {
            string userName = Environment.UserName;
            string mutexName = $@"Local\MangaMeYeah_Mutex_{userName}";
            string pipeName = $"MangaMeYeah_Pipe_{userName}";

            _singleInstanceMutex = new Mutex(true, mutexName, out bool isOnlyInstance);

            if (!isOnlyInstance)
            {
                // Second instance: Forward arguments to the existing instance via Named Pipe
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    client.Connect(1000); // Wait up to 1 second
                    using var writer = new StreamWriter(client, Encoding.UTF8);
                    string payload = string.Join("\n", e.Args);
                    writer.Write(payload);
                    writer.Flush();
                }
                catch
                {
                    // If pipe write failed, ignore
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // First instance: start background listener for arguments forwarded by subsequent launches
            StartPipeListener(pipeName);
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// Windows 10/11 のタブレットペン入力設定(MenuDropAlignment=True)により、
    /// WPF のメニュープルダウンがメニュー項目の右端基準(左方向展開)になってしまう問題を防止し、
    /// MangaMeeya 等の標準 Win32 アプリと同様に「メニュー項目の左端とプルダウンの左端が一致」するよう強制する。
    /// </summary>
    private static void EnsureLeftAlignedMenuDrop()
    {
        try
        {
            var menuDropAlignmentField = typeof(SystemParameters).GetField(
                "_menuDropAlignment",
                BindingFlags.NonPublic | BindingFlags.Static);

            void SetLeftAligned()
            {
                if (SystemParameters.MenuDropAlignment && menuDropAlignmentField != null)
                {
                    menuDropAlignmentField.SetValue(null, false);
                }
            }

            SetLeftAligned();
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.MenuDropAlignment))
                {
                    SetLeftAligned();
                }
            };
        }
        catch
        {
            // Ignore reflection failures
        }
    }

    private static void StartPipeListener(string pipeName)
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? payload = await reader.ReadToEndAsync(token);

                    if (payload != null)
                    {
                        var args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (Current.MainWindow is MainWindow mainWin)
                            {
                                mainWin.ActivateFromAnotherInstance(args);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, token);
                }
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();
        }
        catch { }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        _singleInstanceMutex = null;

        base.OnExit(e);
        Environment.Exit(0);
    }
}
