namespace Focus.Windows.Daemon;

/// <summary>
/// Window operation mode determined by modifier keys held while CAPS is active.
/// Navigate = bare CAPS+direction (existing behavior, default).
/// Move     = CAPS+TAB then direction (move window to adjacent grid cell).
/// Grow     = CAPS+LShift then direction (expand window edge toward that direction).
/// Shrink   = CAPS+LCtrl then direction (contract window edge from that direction).
/// </summary>
internal enum WindowMode { Navigate, Move, Grow, Shrink }

/// <summary>
/// Immutable event record produced by the keyboard hook callback
/// and consumed by CapsLockMonitor via Channel&lt;KeyEvent&gt;.
/// Modifier flags (LShiftHeld, LCtrlHeld, AltHeld) are populated for direction key events;
/// they default to false for CAPSLOCK events (modifier combos are filtered out for CAPSLOCK).
/// Mode defaults to WindowMode.Navigate for backward compatibility — CAPS and number key events
/// use only positional args for VkCode/IsKeyDown/Timestamp and rely on this default.
/// </summary>
internal readonly record struct KeyEvent(
    uint VkCode,
    bool IsKeyDown,
    uint Timestamp,
    bool LShiftHeld = false,
    bool LCtrlHeld = false,
    bool AltHeld = false,
    WindowMode Mode = WindowMode.Navigate);
