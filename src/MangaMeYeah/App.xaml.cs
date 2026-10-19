using System;
using System.IO;
using System.IO.Pipes;
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
