using System.Threading.Channels;

namespace Focus.Windows.Daemon;

/// <summary>
/// Consumes KeyEvent records from a Channel and maintains CAPSLOCK hold/release
/// state machine with optional verbose logging to stderr.
/// Also processes direction key events (arrows + WASD) and number key events (1-9)
/// intercepted while CAPSLOCK is held, suppressing key repeats and invoking the
/// corresponding callbacks for navigation and number-based window selection.
/// </summary>
internal sealed class CapsLockMonitor
{
    private readonly ChannelReader<KeyEvent> _reader;
    private readonly bool _verbose;
    private bool _isHeld;
    private readonly Action? _onHeld;
    private readonly Action? _onReleased;
    private readonly Action<string>? _onDirectionKeyDown;
    private readonly Action<int>? _onNumberKeyDown;

    // Tracks which direction/number keys are currently pressed to suppress key repeats.
    // Cleared on keyup and on ResetState() to prevent stuck keys after sleep/wake.
    // VK code ranges for direction (0x25-0x28, 0x41-0x57) and numbers (0x31-0x39) do not overlap.
    private readonly HashSet<uint> _directionKeysHeld = new();

    public CapsLockMonitor(ChannelReader<KeyEvent> reader, bool verbose,
        Action? onHeld = null, Action? onReleased = null,
        Action<string>? onDirectionKeyDown = null,
        Action<int>? onNumberKeyDown = null)
    {
        _reader = reader;
        _verbose = verbose;
        _onHeld = onHeld;
        _onReleased = onReleased;
        _onDirectionKeyDown = onDirectionKeyDown;
        _onNumberKeyDown = onNumberKeyDown;
    }

    /// <summary>
    /// Maps a number key VK code to the integer 1-9, or null if not a number key.
    /// VK codes 0x31-0x39 correspond to keys 1-9 on the main keyboard row.
    /// </summary>
    private static int? GetNumberFromVkCode(uint vkCode)
    {
        if (vkCode >= 0x31 && vkCode <= 0x39)
            return (int)(vkCode - 0x30);
        return null;
    }

    /// <summary>
    /// Maps a direction key VK code to a canonical direction name,
    /// or null if the VK code is not a direction key.
    /// </summary>
    private static string? GetDirectionName(uint vkCode) => vkCode switch
    {
        0x25 => "left",   // VK_LEFT
        0x26 => "up",     // VK_UP
        0x27 => "right",  // VK_RIGHT
        0x28 => "down",   // VK_DOWN
        0x57 => "up",     // VK_W
        0x41 => "left",   // VK_A
        0x53 => "down",   // VK_S
        0x44 => "right",  // VK_D
        _ => null
    };

    /// <summary>
    /// Builds a modifier prefix string for verbose logging (e.g., "Shift+Ctrl+", or "").
    /// </summary>
    private static string BuildModifierPrefix(KeyEvent evt)
    {
        if (!evt.ShiftHeld && !evt.CtrlHeld && !evt.AltHeld)
            return string.Empty;

        var parts = new List<string>(3);
        if (evt.CtrlHeld)  parts.Add("Ctrl");
        if (evt.AltHeld)   parts.Add("Alt");
        if (evt.ShiftHeld) parts.Add("Shift");
        return string.Join("+", parts) + "+";
    }

    /// <summary>
    /// Returns the display name for a key VK code (used in verbose log).
    /// </summary>
    private static string GetKeyDisplayName(uint vkCode) => vkCode switch
    {
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x57 => "W",
        0x41 => "A",
        0x53 => "S",
        0x44 => "D",
        _ => $"0x{vkCode:X2}"
    };

    /// <summary>
    /// Runs the CAPSLOCK hold/release state machine until cancellation is requested.
    /// Also processes direction key events from the channel.
    /// Designed to run on a worker thread (Task.Run).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var evt in _reader.ReadAllAsync(cancellationToken))
        {
            // CAPSLOCK event (VK_CAPITAL = 0x14)
            if (evt.VkCode == 0x14)
            {
                HandleCapsLockEvent(evt);
                continue;
            }

            // Direction key event
            string? directionName = GetDirectionName(evt.VkCode);
            if (directionName is not null)
            {
                HandleDirectionKeyEvent(evt, directionName);
                continue;
            }

            // Number key event (1-9)
            int? number = GetNumberFromVkCode(evt.VkCode);
            if (number is not null)
            {
                HandleNumberKeyEvent(evt, number.Value);
                continue;
            }

            // Unknown VK code — defensive: ignore
        }
    }

    private void HandleCapsLockEvent(KeyEvent evt)
    {
        if (evt.IsKeyDown)
        {
            // Ignore repeat key-down events (Windows auto-repeat)
            if (_isHeld)
                return;

            _isHeld = true;
            OnCapsLockHeld();
        }
        else
        {
            // Ignore spurious releases
            if (!_isHeld)
                return;

            _isHeld = false;
            OnCapsLockReleased();
        }
    }

    private void HandleDirectionKeyEvent(KeyEvent evt, string directionName)
    {
        if (evt.IsKeyDown)
        {
            // Suppress key repeats silently — only the initial keydown is acted upon
            if (_directionKeysHeld.Contains(evt.VkCode))
                return;

            _directionKeysHeld.Add(evt.VkCode);

            if (_verbose)
            {
                string modifierPrefix = BuildModifierPrefix(evt);
                string keyName = GetKeyDisplayName(evt.VkCode);
                Console.Error.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Direction: {modifierPrefix}{keyName} -> {directionName}");
            }

            _onDirectionKeyDown?.Invoke(directionName);
        }
        else
        {
            // Keyup: reset repeat tracking so next distinct press is processed
            _directionKeysHeld.Remove(evt.VkCode);
            // No verbose log and no callback for keyup
        }
    }

    private void HandleNumberKeyEvent(KeyEvent evt, int number)
    {
        if (evt.IsKeyDown)
        {
            // Suppress key repeats — only the initial keydown is acted upon
            if (_directionKeysHeld.Contains(evt.VkCode))
                return;

            _directionKeysHeld.Add(evt.VkCode);

            if (_verbose)
                Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Number: {number}");

            _onNumberKeyDown?.Invoke(number);
        }
        else
        {
            // Keyup: reset repeat tracking
            _directionKeysHeld.Remove(evt.VkCode);
        }
    }

    /// <summary>
    /// Called when CAPSLOCK is first held down.
    /// </summary>
    private void OnCapsLockHeld()
    {
        if (_verbose)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK held");
        _onHeld?.Invoke();
    }

    /// <summary>
    /// Called when CAPSLOCK is released.
    /// </summary>
    private void OnCapsLockReleased()
    {
        if (_verbose)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK released");
        _onReleased?.Invoke();
    }

    /// <summary>
    /// Resets the CAPSLOCK held state and clears direction key tracking.
    /// Called after hook reinstall on sleep/wake to prevent stuck-held condition.
    /// </summary>
    public void ResetState()
    {
        _isHeld = false;
        _directionKeysHeld.Clear();
    }
}
