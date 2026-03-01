using System.Diagnostics;

namespace Focus.Windows.Daemon;

internal static class DaemonMutex
{
    private const string MutexName = @"Global\focus-daemon";

    /// <summary>
    /// Acquires the single-instance named mutex.
    /// If another daemon process owns the mutex, kills that process and re-acquires.
    /// The caller MUST keep the returned Mutex reference alive for the process lifetime.
    /// </summary>
    public static Mutex AcquireOrReplace()
    {
        var mutex = new Mutex(false, MutexName, out bool createdNew);

        if (createdNew)
        {
            // We created it — take ownership
            mutex.WaitOne(0);
            return mutex;
        }

        // Another daemon instance is running — kill it
        int ownPid = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("focus"))
        {
            if (p.Id == ownPid)
                continue;

            try
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            catch
            {
                // Process may have already exited — ignore
            }
        }

        // Dispose the old mutex handle and re-acquire with ownership
        mutex.Dispose();
        mutex = new Mutex(true, MutexName, out _);
        return mutex;
    }

    /// <summary>
    /// Safely releases and disposes the daemon mutex.
    /// </summary>
    public static void Release(Mutex mutex)
    {
        try
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
