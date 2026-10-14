using System.Net.Sockets;
using System.Text;
using BrowserWrangler.Core;

namespace BrowserWrangler.Linux.Services;

/// <summary>
/// Keeps one config window per user: the first instance listens on a Unix socket and later
/// launches ask it to come to the front instead of opening a second window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string ActivateMessage = "activate\n";
    private readonly Socket _listener;
    private readonly string _path;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    public static string SocketPath
    {
        get
        {
            string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            string dir = !string.IsNullOrEmpty(runtime) && Directory.Exists(runtime) ? runtime : Path.GetTempPath();
            return Path.Combine(dir, $"{AppInfo.LinuxAppId}-{Environment.UserName}.sock");
        }
    }

    /// <summary>Returns null when another instance is running (and was told to activate).</summary>
    public static SingleInstance? TryAcquire()
    {
        string path = SocketPath;
        if (TrySignalExisting(path))
        {
            return null;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(4);
        }
        catch (SocketException)
        {
            // lost a race with another instance starting at the same time
            listener.Dispose();
            return TrySignalExisting(path) ? null : new SingleInstance(new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified), path);
        }

        return new SingleInstance(listener, path);
    }

    public void StartListening(Action onActivate)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using Socket client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
                    var buffer = new byte[64];
                    int read = await client.ReceiveAsync(buffer, SocketFlags.None, _cts.Token).ConfigureAwait(false);
                    if (Encoding.ASCII.GetString(buffer, 0, read) == ActivateMessage)
                    {
                        onActivate();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        });
    }

    private static bool TrySignalExisting(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            socket.Send(Encoding.ASCII.GetBytes(ActivateMessage));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
