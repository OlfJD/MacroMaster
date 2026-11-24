using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacroMaster.Core.Native;

/// <summary>
/// One MacroMaster per data folder and signed-in session. Two copies on the same profiles each
/// saved their own (older) list over the other's, so macros made in one window were gone as soon as
/// the other copy saved anything, and both ran every hotkey. A second launch asks the running copy
/// to show its window and quits.
///
/// Done with one named event: whoever creates it first is "the" copy and waits on it, a later launch
/// finds it and sets it. Creating it is atomic, so two copies started at the same moment (e.g. two
/// start entries at sign-in) can't both decide to quit, as they could when each looked for the other
/// one's process. The event's security lets the signed-in user set it from a normal (medium
/// integrity) process, so a normal launch can also reach a copy that runs as administrator.
/// A copy on another data folder (MACROMASTER_DATA_DIR, for trying things out) runs alongside.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly SafeWaitHandle? _event;
    private Thread? _listener;
    private volatile bool _disposed;

    /// <summary>True for the copy that runs; false when another copy already runs on this data folder.</summary>
    public bool IsFirst { get; }

    private SingleInstance(SafeWaitHandle? handle, bool isFirst)
    {
        _event = handle;
        IsFirst = isFirst;
    }

    /// <summary>Claims the data folder for this copy. Never throws: if Windows says no for an unknown reason, this copy runs.</summary>
    public static SingleInstance Claim(string dataDirectory)
    {
        string name = NameFor(dataDirectory);
        string user = WindowsIdentity.GetCurrent().User?.Value ?? "";

        // The signed-in user may do anything with it (also from a normal process while the running
        // copy is elevated); medium integrity label for the same reason
        foreach (string? sddl in new[]
                 {
                     $"D:(A;;0x1F0003;;;{user})(A;;0x1F0003;;;SY)S:(ML;;NW;;;ME)",
                     $"D:(A;;0x1F0003;;;{user})(A;;0x1F0003;;;SY)",
                     null // Windows' default security
                 })
        {
            if (sddl != null && user.Length == 0) continue;
            var handle = Create(name, sddl, out int error);
            if (!handle.IsInvalid) return new SingleInstance(handle, isFirst: error != ERROR_ALREADY_EXISTS);
            handle.Dispose();
            // It exists, but this process may not use it (e.g. made by an older version running as administrator)
            if (error == ERROR_ACCESS_DENIED) return new SingleInstance(null, isFirst: false);
        }
        return new SingleInstance(null, isFirst: true);
    }

    /// <summary>"Local\" = this sign-in session only, so another user's copy (fast user switching) doesn't count.</summary>
    internal static string NameFor(string dataDirectory)
    {
        string folder = Path.GetFullPath(dataDirectory).TrimEnd('\\', '/').ToLowerInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
        return $@"Local\MacroMaster-{hash}";
    }

    /// <summary>The running copy: call <paramref name="onShowRequested"/> (on a background thread) whenever another launch asks.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        if (!IsFirst || _event == null || _listener != null) return;
        _listener = new Thread(() =>
        {
            while (WaitForSingleObject(_event, INFINITE) == WAIT_OBJECT_0 && !_disposed)
            {
                try { onShowRequested(); } catch { /* must keep listening */ }
            }
        })
        { IsBackground = true, Name = "MacroMaster single instance" };
        _listener.Start();
    }

    /// <summary>A later launch: ask the running copy to show its window. False if it can't be reached.</summary>
    public bool AskRunningCopyToShow()
    {
        if (IsFirst || _event == null) return false;
        AllowSetForegroundWindow(ASFW_ANY); // the user just started us, so the running copy may take the front
        return SetEvent(_event);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_listener != null && _event != null)
        {
            SetEvent(_event); // wakes the listener, which sees _disposed and ends
            _listener.Join(1000);
        }
        _event?.Dispose();
    }

    private static SafeWaitHandle Create(string name, string? sddl, out int error)
    {
        IntPtr sd = IntPtr.Zero;
        try
        {
            if (sddl == null || !ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out sd, out _))
            {
                var plain = CreateEventW(IntPtr.Zero, false, false, name);
                error = Marshal.GetLastWin32Error();
                return plain;
            }
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            var handle = CreateEventW(ref sa, false, false, name);
            error = Marshal.GetLastWin32Error();
            return handle;
        }
        finally
        {
            if (sd != IntPtr.Zero) LocalFree(sd);
        }
    }

    #region Win32

    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_ALREADY_EXISTS = 183;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_OBJECT_0 = 0;
    private const int ASFW_ANY = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, out uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateEventW(ref SECURITY_ATTRIBUTES sa, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateEventW(IntPtr sa, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(SafeWaitHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint ms);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    #endregion
}
