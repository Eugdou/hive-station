using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WorkingCenter.Core;

/// <summary>One writer per portable data directory; later launches signal the first.</summary>
internal sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _registration;
    private bool _ownsMutex;

    public bool TryAcquire(Action activate)
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        var name = @"Local\HiveStation-" + hash;

        // Create the signal before competing for the mutex so an early second
        // launch can leave a signal for the first launch to consume after startup.
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
        _mutex = new Mutex(false, name);
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }

        if (!_ownsMutex)
        {
            _activation.Set();
            return false;
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
        {
            try { activate(); }
            catch (InvalidOperationException) { /* dispatcher already shutting down */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _activation?.Dispose();
        _activation = null;
        if (_ownsMutex)
        {
            _mutex!.ReleaseMutex();
            _ownsMutex = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
