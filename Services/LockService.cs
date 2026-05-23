using System;
using System.Runtime.InteropServices;

namespace SignalLock.Services;

/// <summary>
/// All shell-out and Win32 entry points for locking the screen live here.
/// No other module is allowed to call <c>user32!LockWorkStation</c> directly.
/// </summary>
public sealed class LockService
{
    public enum Strategy
    {
        LockWorkStation,
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public Strategy? Lock()
    {
        try
        {
            var ok = LockWorkStation();
            if (ok)
            {
                Log.Lock.Info("Locked via LockWorkStation");
                return Strategy.LockWorkStation;
            }
            var err = Marshal.GetLastWin32Error();
            Log.Lock.Error($"LockWorkStation failed (Win32 error {err})");
            return null;
        }
        catch (Exception ex)
        {
            Log.Lock.Error($"LockWorkStation threw: {ex.Message}");
            return null;
        }
    }
}
