namespace Focus.Windows.Daemon;

/// <summary>
/// Immutable event record produced by the keyboard hook callback
/// and consumed by CapsLockMonitor via Channel&lt;KeyEvent&gt;.
/// </summary>
internal readonly record struct KeyEvent(uint VkCode, bool IsKeyDown, uint Timestamp);
