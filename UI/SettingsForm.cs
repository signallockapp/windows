using System;
using System.Drawing;
using System.Windows.Forms;

namespace SignalLock.UI;

public sealed class SettingsForm : Form
{
    private readonly AppState _state;

    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _awayDelay = new();
    private readonly NumericUpDown _smoothing = new();
    private readonly CheckBox _startAtLogin = new();
    private readonly Label _trustedLabel = new();
    private readonly Button _forgetButton = new();
    private bool _suppressEvents;

    public SettingsForm(AppState state)
    {
        _state = state;

        Text = "SignalLock Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 360);
        Padding = new Padding(20);
        Font = SystemFonts.MessageBoxFont!;

        BuildLayout();
        ApplySettingsToUI();
        WireEvents();

        _state.StateChanged += OnAppStateChanged;
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = true,
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

        // RSSI threshold
        layout.Controls.Add(new Label
        {
            Text = "RSSI threshold (dBm)\nSignals weaker than this are treated as 'far'.",
            AutoSize = true,
        }, 0, 0);
        _threshold.Minimum = -100;
        _threshold.Maximum = -40;
        _threshold.Increment = 1;
        _threshold.Width = 100;
        layout.Controls.Add(_threshold, 1, 0);

        // Away delay
        layout.Controls.Add(new Label
        {
            Text = "Away delay (seconds)\nLock fires only after device is away this long.",
            AutoSize = true,
        }, 0, 1);
        _awayDelay.Minimum = 5;
        _awayDelay.Maximum = 120;
        _awayDelay.Increment = 5;
        _awayDelay.Width = 100;
        layout.Controls.Add(_awayDelay, 1, 1);

        // Smoothing window
        layout.Controls.Add(new Label
        {
            Text = "Smoothing window (samples)\nMoving-average size.",
            AutoSize = true,
        }, 0, 2);
        _smoothing.Minimum = 1;
        _smoothing.Maximum = 20;
        _smoothing.Increment = 1;
        _smoothing.Width = 100;
        layout.Controls.Add(_smoothing, 1, 2);

        // Start at login
        _startAtLogin.AutoSize = true;
        _startAtLogin.Text = "Start at login";
        layout.Controls.Add(_startAtLogin, 0, 3);
        layout.SetColumnSpan(_startAtLogin, 2);

        // Trusted device
        var devicePanel = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        devicePanel.Controls.Add(new Label { Text = "Trusted device:", AutoSize = true });
        _trustedLabel.AutoSize = true;
        _trustedLabel.Padding = new Padding(8, 0, 8, 0);
        devicePanel.Controls.Add(_trustedLabel);
        _forgetButton.Text = "Forget";
        _forgetButton.AutoSize = true;
        devicePanel.Controls.Add(_forgetButton);
        layout.Controls.Add(devicePanel, 0, 4);
        layout.SetColumnSpan(devicePanel, 2);

        // Close
        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };
        close.Click += (_, _) => Close();
        AcceptButton = close;
        CancelButton = close;
        layout.Controls.Add(close, 1, 5);

        Controls.Add(layout);
    }

    private void WireEvents()
    {
        _threshold.ValueChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _state.UpdateSettings(s => s.RssiThreshold = (int)_threshold.Value);
        };
        _awayDelay.ValueChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _state.UpdateSettings(s => s.AwayDelaySeconds = (int)_awayDelay.Value);
        };
        _smoothing.ValueChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _state.UpdateSettings(s => s.RssiSmoothingWindow = (int)_smoothing.Value);
        };
        _startAtLogin.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _state.SetStartAtLogin(_startAtLogin.Checked);
        };
        _forgetButton.Click += (_, _) =>
        {
            var ok = MessageBox.Show(this,
                "Forget the current trusted device? You will need to pick a new one before monitoring can resume.",
                "Forget device",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok == DialogResult.OK) _state.ClearTrustedDevice();
        };
    }

    private void OnAppStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(ApplySettingsToUI); return; }
        ApplySettingsToUI();
    }

    private void ApplySettingsToUI()
    {
        _suppressEvents = true;
        try
        {
            _threshold.Value = Clamp(_state.Settings.RssiThreshold, _threshold.Minimum, _threshold.Maximum);
            _awayDelay.Value = Clamp(_state.Settings.AwayDelaySeconds, _awayDelay.Minimum, _awayDelay.Maximum);
            _smoothing.Value = Clamp(_state.Settings.RssiSmoothingWindow, _smoothing.Minimum, _smoothing.Maximum);
            _startAtLogin.Checked = _state.Settings.StartAtLogin;

            if (_state.TrustedDevice is not null)
            {
                _trustedLabel.Text = $"{_state.TrustedDevice.Name}  ({_state.TrustedDevice.Identifier})";
                _forgetButton.Enabled = true;
            }
            else
            {
                _trustedLabel.Text = "(none selected)";
                _forgetButton.Enabled = false;
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private static decimal Clamp(int v, decimal min, decimal max) =>
        Math.Min(Math.Max(v, min), max);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _state.StateChanged -= OnAppStateChanged;
        base.OnFormClosed(e);
    }
}
