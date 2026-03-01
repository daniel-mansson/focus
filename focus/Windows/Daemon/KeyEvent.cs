namespace Focus.Windows.Daemon;

/// <summary>
/// Immutable event record produced by the keyboard hook callback
/// and consumed by CapsLockMonitor via Channel&lt;KeyEvent&gt;.
/// Modifier flags (ShiftHeld, CtrlHeld, AltHeld) are populated for direction key events;
/// they default to false for CAPSLOCK events (modifier combos are filtered out for CAPSLOCK).
/// </summary>
internal readonly record struct KeyEvent(
    uint VkCode,
    bool IsKeyDown,
    uint Timestamp,
    bool ShiftHeld = false,
    bool CtrlHeld = false,
    bool AltHeld = false);
