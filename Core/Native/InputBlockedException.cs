namespace MacroMaster.Core.Native;

/// <summary>
/// Windows refused a key press, click or mouse move a macro sent (SendInput accepted nothing).
/// It does that on the lock screen and the UAC prompt, while another program blocks input, and
/// often for a window that runs as administrator. A run that gets this stops instead of carrying on
/// without anything reaching the app.
/// </summary>
public sealed class InputBlockedException : Exception
{
    public InputBlockedException() : base("Windows refused the simulated input") { }
}
