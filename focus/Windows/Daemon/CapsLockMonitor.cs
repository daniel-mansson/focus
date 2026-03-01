using System.Threading.Channels;

namespace Focus.Windows.Daemon;

/// <summary>
/// Consumes KeyEvent records from a Channel and maintains CAPSLOCK hold/release
/// state machine with optional verbose logging to stderr.
/// </summary>
internal sealed class CapsLockMonitor
{
    private readonly ChannelReader<KeyEvent> _reader;
    private readonly bool _verbose;
    private bool _isHeld;

    public CapsLockMonitor(ChannelReader<KeyEvent> reader, bool verbose)
    {
        _reader = reader;
        _verbose = verbose;
    }

    /// <summary>
    /// Runs the CAPSLOCK hold/release state machine until cancellation is requested.
    /// Designed to run on a worker thread (Task.Run).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var evt in _reader.ReadAllAsync(cancellationToken))
        {
            if (evt.IsKeyDown)
            {
                // Ignore repeat key-down events (Windows auto-repeat)
                if (_isHeld)
                    continue;

                _isHeld = true;
                OnCapsLockHeld();
            }
            else
            {
                // Ignore spurious releases
                if (!_isHeld)
                    continue;

                _isHeld = false;
                OnCapsLockReleased();
            }
        }
    }

    /// <summary>
    /// Called when CAPSLOCK is first held down.
    /// Phase 6 will hook overlay show logic here.
    /// </summary>
    private void OnCapsLockHeld()
    {
        if (_verbose)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK held");
    }

    /// <summary>
    /// Called when CAPSLOCK is released.
    /// Phase 6 will hook overlay hide logic here.
    /// </summary>
    private void OnCapsLockReleased()
    {
        if (_verbose)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK released");
    }

    /// <summary>
    /// Resets the CAPSLOCK held state.
    /// Called after hook reinstall on sleep/wake to prevent stuck-held condition.
    /// </summary>
    public void ResetState()
    {
        _isHeld = false;
    }
}
