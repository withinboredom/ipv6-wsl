using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using SharpPcap.WinDivert;

namespace ipv6dup_ui
{
    public partial class Form1 : Form
    {
        private ILiveDevice? _internetDevice = null;
        private PhysicalAddress? _rewriteDestination = null;
        private IPAddress[] _addresses = null;

        private bool _enabled = false;
        private long _packets = 0;

        public Form1()
        {
            InitializeComponent();
            var devices = CaptureDeviceList.Instance;
            var hostname = System.Net.Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostname);
            Console.WriteLine($"Detected {addresses.Length} IP addresses: {string.Join(", ",addresses.Select(e => e.ToString()))}");

            _addresses = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

            foreach (var device in devices)
            {
                if (device.MacAddress == null) continue;
                Console.WriteLine($"Found valid device: {device.Description} with mac address {device.MacAddress} [{device.Name}]");
                deviceBox.Items.Add($"{device.Description} [{device.MacAddress}]");
            }
        }

        private void ChangedInternetDevice(object sender, EventArgs e)
        {
            var devices = LibPcapLiveDeviceList.Instance;

            foreach (var device in devices)
            {
                if (deviceBox.SelectedItem == null || device.MacAddress == null) continue;
                if (!deviceBox.SelectedItem.ToString()!.Contains(device.MacAddress.ToString())) continue;
                
                if (_internetDevice != null)
                {
                    Console.WriteLine($"Stopping capture on {_internetDevice.Description}.");
                    _internetDevice.StopCapture();
                    _internetDevice.Close();
                }

                _internetDevice = device;
                _internetDevice.Open();
                _internetDevice.OnPacketArrival += InternetDeviceOnOnPacketArrival;
                _internetDevice.StartCapture();
                Console.WriteLine($"Starting capture on {_internetDevice.Description}.");
            }
        }

        private void InternetDeviceOnOnPacketArrival(object sender, PacketCapture e)
        {
            if (!_enabled) return;

            // parse the packet
            var rawPacket = e.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

            if (packet is not EthernetPacket eth) return;
            if (eth.Type != EthernetType.IPv6 ||
                !Equals(eth.DestinationHardwareAddress, _internetDevice?.MacAddress)) return;
            var checkedPacket = false;

            var ipv6 = packet.Extract<IPv6Packet>();
            if (ipv6 != null)
            {
                if (_addresses.Contains(ipv6.DestinationAddress))
                {
                    return;
                }

                Console.WriteLine($"Going to rewrite ipv6 packet for {ipv6.DestinationAddress} to {_rewriteDestination}");
                checkedPacket = true;
            }

            if (!checkedPacket) return;

            eth.DestinationHardwareAddress = _rewriteDestination;

            try
            {
                _internetDevice?.SendPacket(eth.Bytes);
                _packets += 1;
            }
            catch
            {
                Console.WriteLine("Failed to write packet!");
            }
        }

        private void maskedTextBox1_MaskInputRejected(object sender, MaskInputRejectedEventArgs e)
        {

        }

        private void StartButtonClick(object sender, EventArgs e)
        {
            _rewriteDestination = PhysicalAddress.Parse(maskedTextBox1.Text);

            //Console.WriteLine(BitConverter.ToString(_rewriteDestination));

            if (_internetDevice == null || _rewriteDestination == null) return;

            StopButton.Enabled = true;
            startButton.Enabled = false;
            _enabled = true;
        }

        private void maskedTextBox1_TextChanged(object sender, EventArgs e)
        {
            StringBuilder value = new StringBuilder();
            foreach (var t in maskedTextBox1.Text)
            {
                if (t is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F') value.Append(t.ToString().ToUpperInvariant());
            }

            if (value.Length != maskedTextBox1.Text.Length)
            {
                maskedTextBox1.Text = value.ToString();
            }
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            label4.Text = $"Packets Handled {_packets:N0}";
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            _enabled = false;
            StopButton.Enabled = false;
            startButton.Enabled = true;
        }
    }
}