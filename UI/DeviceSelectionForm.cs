using System;
using System.Drawing;
using System.Windows.Forms;

namespace SignalLock.UI;

public sealed class DeviceSelectionForm : Form
{
    private readonly AppState _state;
    private readonly ListView _list = new();
    private readonly Label _hint = new();
    private readonly Button _selectButton = new();
    private readonly Button _cancelButton = new();

    public DeviceSelectionForm(AppState state)
    {
        _state = state;

        Text = "Select Trusted Device";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(440, 360);
        Padding = new Padding(12);
        Font = SystemFonts.MessageBoxFont!;

        BuildLayout();
        WireEvents();

        _state.StateChanged += OnAppStateChanged;
    }

    private void BuildLayout()
    {
        _hint.Dock = DockStyle.Top;
        _hint.AutoSize = false;
        _hint.Height = 40;
        _hint.Padding = new Padding(0, 0, 0, 6);
        _hint.Text = "Bring your trusted device close to this PC. Pick the entry with the strongest signal.";
        Controls.Add(_hint);

        _list.View = View.Details;
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("RSSI", 80);
        _list.Columns.Add("Address", 240);
        Controls.Add(_list);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        _selectButton.Text = "Select";
        _selectButton.Enabled = false;
        _selectButton.AutoSize = true;
        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        buttonPanel.Controls.Add(_selectButton);
        buttonPanel.Controls.Add(_cancelButton);
        Controls.Add(buttonPanel);

        AcceptButton = _selectButton;
        CancelButton = _cancelButton;

        // Layout order matters: top → bottom → fill.
        Controls.SetChildIndex(_hint, 0);
        Controls.SetChildIndex(buttonPanel, 1);
        Controls.SetChildIndex(_list, 2);
    }

    private void WireEvents()
    {
        _list.SelectedIndexChanged += (_, _) =>
            _selectButton.Enabled = _list.SelectedItems.Count == 1;

        _list.DoubleClick += (_, _) => SelectFromList();
        _selectButton.Click += (_, _) => SelectFromList();
        _cancelButton.Click += (_, _) => Close();
    }

    private void SelectFromList()
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is not DiscoveredDevice device) return;
        _state.SelectTrustedDevice(device);
        Close();
    }

    private void OnAppStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(RefreshList); return; }
        RefreshList();
    }

    private void RefreshList()
    {
        _hint.Text = HintForAvailability();

        // Preserve current selection when possible.
        var selectedAddress = (_list.SelectedItems.Count == 1
            && _list.SelectedItems[0].Tag is DiscoveredDevice d) ? d.Address : (ulong?)null;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var dev in _state.DiscoveredDevices)
            {
                var lvi = new ListViewItem(new[]
                {
                    dev.Name,
                    $"{dev.Rssi} dBm",
                    dev.Identifier,
                })
                {
                    Tag = dev,
                };
                if (selectedAddress.HasValue && dev.Address == selectedAddress.Value)
                {
                    lvi.Selected = true;
                }
                _list.Items.Add(lvi);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _selectButton.Enabled = _list.SelectedItems.Count == 1;
    }

    private string HintForAvailability() => _state.Availability switch
    {
        BluetoothAvailability.Ready =>
            "Bring your trusted device close. Pick the entry with the strongest (least negative) RSSI.",
        BluetoothAvailability.PoweredOff =>
            "Bluetooth is turned off. Enable it from Settings → Devices → Bluetooth.",
        BluetoothAvailability.Unauthorized =>
            "Bluetooth is blocked by policy. Check Settings → Privacy & security → Bluetooth.",
        BluetoothAvailability.Unsupported =>
            "This PC does not support Bluetooth Low Energy.",
        _ => "Initializing Bluetooth…",
    };

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _state.StartDeviceDiscovery();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _state.StopDeviceDiscovery();
        _state.StateChanged -= OnAppStateChanged;
        base.OnFormClosed(e);
    }
}
