using System;
using Microsoft.Win32;

namespace SignalLock.Services;

/// <summary>
/// Toggles auto-start on login by writing the executable path under
/// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run. Per-user, no admin
/// required, no Task Scheduler dependency.
/// </summary>
public sealed class LoginItemService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SignalLock";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Login-item check failed: {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                Log.App.Error("Could not open or create the Run registry key.");
                return false;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.App.Error($"Login-item toggle failed: {ex.Message}");
            return false;
        }
    }
}
