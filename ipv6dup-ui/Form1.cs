using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;
using SharpPcap.LibPcap;

namespace ipv6dup_ui
{
	public partial class Form1 : Form
	{
		/// <summary>
		///   The injector to inject packets with
		/// </summary>
		private readonly Injector _injector;

		/// <summary>
		///   The pinger to determine current status
		/// </summary>
		private readonly Pinger _pinger;

		/// <summary>
		///   Whether we are connected or not
		/// </summary>
		private bool _connected;

		/// <summary>
		///   Whether we are enabled or not
		/// </summary>
		private bool _enabled;

		private bool _loading = true;

		private const string RegistryValue = "WSL IPv6 Enabler";

		/// <summary>
		///   Create a form
		/// </summary>
		public Form1()
		{
			InitializeComponent();
			var devices = LibPcapLiveDeviceList.Instance;
			var hostname = Dns.GetHostName();
			var addresses = Dns.GetHostAddresses(hostname);

			_pinger = new Pinger("2a01:4f9:6b:5601::2");
			_injector = new Injector
			{
				Addresses = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetworkV6).ToArray()
			};

			if (!string.IsNullOrEmpty(PreviousValues.Default.MacAddress))
			{
				_injector.RewriteDestination = PhysicalAddress.Parse(PreviousValues.Default.MacAddress);
				maskedTextBox1.Text = PreviousValues.Default.MacAddress;
			}

			foreach (var device in devices)
			{
				wslBox.Items.Add(device.Description);
				if (device.Description == PreviousValues.Default.InternetDevice)
				{
					_injector.InternetDevice = device;
					wslBox.SelectedIndex = wslBox.Items.Count - 1;
				}

				if (device.MacAddress == null)
				{
					continue;
				}

				deviceBox.Items.Add($"{device.Description} [{device.MacAddress}]");
				if (PreviousValues.Default.InternalDevice.Contains(device.MacAddress.ToString()))
				{
					_injector.InternalDevice = device;
					deviceBox.SelectedIndex = deviceBox.Items.Count - 1;
				}
			}

			var reg = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
			var onStartup = reg?.GetValue(RegistryValue)?.ToString();
			if(!string.IsNullOrEmpty(onStartup))
			{
				checkBox1.Checked = true;
				StartButtonClick(null!, null!);
				WindowState = FormWindowState.Minimized;
			}

			_loading = false;
		}

		/// <summary>
		///   The user has changed the internal device, so update state
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ChangedInternalDevice(object sender, EventArgs e)
		{
			var devices = LibPcapLiveDeviceList.Instance;

			foreach (var device in devices)
			{
				if (deviceBox.SelectedItem == null || device.MacAddress == null)
				{
					continue;
				}

				if (!deviceBox.SelectedItem.ToString()!.Contains(device.MacAddress.ToString()))
				{
					continue;
				}

				_injector.InternalDevice = device;
			}
		}

		/// <summary>
		///   The user has clicked the start button
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void StartButtonClick(object sender, EventArgs e)
		{
			_injector.RewriteDestination = PhysicalAddress.Parse(maskedTextBox1.Text);

			if (!_injector.Ready)
			{
				MessageBox.Show(@"Please select devices and enter a mac address", @"Not ready yet", MessageBoxButtons.OK,
					MessageBoxIcon.Stop);
				return;
			}

			StopButton.Enabled = true;
			startButton.Enabled = false;
			_injector.Enabled = true;
			_enabled = true;

			PreviousValues.Default.MacAddress = _injector.RewriteDestination.ToString();
			PreviousValues.Default.InternalDevice = _injector.InternalDevice?.MacAddress.ToString();
			PreviousValues.Default.InternetDevice = _injector.InternetDevice?.Description;
			PreviousValues.Default.Save();
		}

		/// <summary>
		///   Update the textbox with only valid characters
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void MacAddressChanged(object sender, EventArgs e)
		{
			var value = new StringBuilder();
			foreach (var t in maskedTextBox1.Text)
			{
				if (t is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
				{
					value.Append(t.ToString().ToUpperInvariant());
				}
			}

			if (value.Length != maskedTextBox1.Text.Length)
			{
				maskedTextBox1.Text = value.ToString();
			}
		}

		/// <summary>
		///   Update stats and status
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void TimerTick(object sender, EventArgs e)
		{
			var connection = "no";
			if (_pinger.CurrentStatus == Pinger.Status.Degraded ||
			    (_pinger.LastSeq - _pinger.LastDup < 10 && _pinger.LastDup > 0))
			{
				connection = "degraded";
			}

			if (_pinger.CurrentStatus == Pinger.Status.Connected)
			{
				connection = "yes";
				_connected = true;
				_injector.Enabled = false;
			}
			else
			{
				_connected = false;
				_injector.Enabled = _enabled;
			}

			if (_injector.Enabled && !_connected)
			{
				_pinger.Helping = true;
			}

			label4.Text = $@"Packets Handled: {_injector.HandledPackets:N0}";
			label2.Text = $@" Packets Captured:  {_injector.CapturedPackets:N0}";
			label6.Text = $@"IPv6 Working: {connection}";
		}

		/// <summary>
		///   User has clicked the stop button
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void StopButton_Click(object sender, EventArgs e)
		{
			_injector.Enabled = false;
			StopButton.Enabled = false;
			startButton.Enabled = true;
			_enabled = false;
		}

		/// <summary>
		///   User has selected a new internet device
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void InternetDeviceChanged(object sender, EventArgs e)
		{
			var devices = LibPcapLiveDeviceList.Instance;

			foreach (var device in devices)
			{
				if (wslBox.SelectedItem == null)
				{
					continue;
				}

				if (!wslBox.SelectedItem.ToString()!.Contains(device.Description))
				{
					continue;
				}

				_injector.InternetDevice = device;
			}
		}

		private void Form1_Resize(object sender, EventArgs e)
		{
			if (WindowState != FormWindowState.Minimized)
			{
				return;
			}

			ShowInTaskbar = false;
			notifyIcon1.Visible = true;
			notifyIcon1.ShowBalloonTip(0);
		}

		private void notifyIcon1_DoubleClick(object sender, EventArgs e)
		{
			if (WindowState != FormWindowState.Minimized)
			{
				return;
			}

			ShowInTaskbar = true;
			notifyIcon1.Visible = false;
			WindowState = FormWindowState.Normal;
		}

		private void checkBox1_CheckedChanged(object sender, EventArgs e)
		{
			if (_loading) return;

			var reg = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
			switch (checkBox1.CheckState)
			{
				case CheckState.Checked:
					reg?.SetValue("WSL IPv6 Enabler", Application.ExecutablePath.ToString());
					break;
				case CheckState.Unchecked:
					reg?.DeleteValue("WSL IPv6 Enabler");
					break;
				case CheckState.Indeterminate:
					throw new NotImplementedException();
			}
		}
	}
}
