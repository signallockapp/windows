using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SignalLock.UI;

public sealed class TrayController : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private SettingsForm? _settingsForm;
    private DeviceSelectionForm? _selectionForm;

    public TrayController(AppState state)
    {
        _state = state;
        _menu = new ContextMenuStrip();
        _icon = new NotifyIcon
        {
            Visible = true,
            Text = "SignalLock",
            Icon = BuildTrayIcon(),
            ContextMenuStrip = _menu,
        };

        _menu.Opening += (_, _) => RebuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // Mirror right-click behaviour for left-click — feels more native to many users.
                _icon.ContextMenuStrip?.Show(Cursor.Position);
            }
        };

        _state.StateChanged += (_, _) => UpdateTooltip();
        RebuildMenu();
        UpdateTooltip();
    }

    private static Icon BuildTrayIcon()
    {
        // Programmatically render the brand lock to a 32×32 bitmap.
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, 32, 32),
                Color.FromArgb(110, 168, 255),
                Color.FromArgb(74, 139, 255),
                45f);
            g.FillRectangle(brush, 0, 0, 32, 32);

            using var pen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // Lock body
            g.DrawRectangle(pen, 8, 14, 16, 12);
            // Lock shackle
            g.DrawArc(pen, 10, 7, 12, 12, 180, 180);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private string StatusLine() =>
        _state.IsMonitoring
            ? (_state.IsAway ? "Status: Away (waiting to lock)" : "Status: Monitoring")
            : "Status: Paused";

    private string LastSeenText()
    {
        if (_state.LastSeen is null) return "—";
        var seconds = (int)(DateTime.UtcNow - _state.LastSeen.Value).TotalSeconds;
        if (seconds < 5) return "just now";
        if (seconds < 60) return $"{seconds} seconds ago";
        return $"{seconds / 60} min ago";
    }

    private string BtWarning() => _state.Availability switch
    {
        BluetoothAvailability.PoweredOff => "⚠ Bluetooth is off",
        BluetoothAvailability.Unauthorized => "⚠ Bluetooth blocked by policy",
        BluetoothAvailability.Unsupported => "⚠ BLE not supported on this PC",
        BluetoothAvailability.Unknown => "⚠ Bluetooth state unknown",
        _ => string.Empty,
    };

    private void UpdateTooltip()
    {
        // System tray tooltips have a 63-char limit (NotifyIcon.Text).
        var rssi = _state.CurrentRssi.HasValue ? $"{_state.CurrentRssi} dBm" : "—";
        var name = _state.TrustedDevice?.Name ?? "(none)";
        var line = $"SignalLock — {(_state.IsMonitoring ? "Monitoring" : "Paused")} · {name} · {rssi}";
        if (line.Length > 63) line = line[..63];
        _icon.Text = line;
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        AddInfo("SignalLock");
        AddInfo(StatusLine());
        AddInfo("Trusted Device: " + (_state.TrustedDevice?.Name ?? "(none)"));
        AddInfo("Current Signal: " + (_state.CurrentRssi.HasValue ? $"{_state.CurrentRssi} dBm" : "—"));
        AddInfo("Last Seen: " + LastSeenText());

        if (_state.LastTriggeredLockAt is not null)
        {
            AddInfo($"Last Auto-Lock: {_state.LastTriggeredLockAt.Value.ToLocalTime():HH:mm:ss}");
        }

        var warning = BtWarning();
        if (!string.IsNullOrEmpty(warning)) AddInfo(warning);

        _menu.Items.Add(new ToolStripSeparator());

        if (_state.IsMonitoring)
        {
            AddAction("Pause Monitoring", () => _state.StopMonitoring());
        }
        else
        {
            var item = AddAction("Start Monitoring", () => _state.StartMonitoring());
            item.Enabled = _state.TrustedDevice is not null
                           && _state.Availability == BluetoothAvailability.Ready;
        }

        AddAction("Select Trusted Device…", OpenDeviceSelection);
        AddAction("Settings…", OpenSettings);
        AddAction("Test Lock", () => _state.TestLock());

        _menu.Items.Add(new ToolStripSeparator());
        AddAction("Quit SignalLock", () => Application.Exit());
    }

    private ToolStripMenuItem AddInfo(string text)
    {
        var mi = new ToolStripMenuItem(text) { Enabled = false };
        _menu.Items.Add(mi);
        return mi;
    }

    private ToolStripMenuItem AddAction(string text, Action onClick)
    {
        var mi = new ToolStripMenuItem(text);
        mi.Click += (_, _) => onClick();
        _menu.Items.Add(mi);
        return mi;
    }

    private void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_state);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenDeviceSelection()
    {
        if (_selectionForm is null || _selectionForm.IsDisposed)
        {
            _selectionForm = new DeviceSelectionForm(_state);
            _selectionForm.FormClosed += (_, _) => _selectionForm = null;
        }
        _selectionForm.Show();
        _selectionForm.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _settingsForm?.Dispose();
        _selectionForm?.Dispose();
    }
}
