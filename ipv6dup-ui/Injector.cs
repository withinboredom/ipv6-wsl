using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ipv6dup_ui
{
	/// <summary>
	///   Injects packets!
	/// </summary>
	internal class Injector
	{
		/// <summary>
		///   DEBUG: set to true to record injected packets
		/// </summary>
		private const bool Record = false;

		/// <summary>
		///   DEBUG: allows recording injected packets
		/// </summary>
		private readonly CaptureFileWriterDevice? _writer;

		/// <summary>
		///   Internal device where we will inject packets
		/// </summary>
		private ILiveDevice? _internalDevice;

		/// <summary>
		///   External device where we will look for incoming packets
		/// </summary>
		private ILiveDevice? _internetDevice;

		/// <summary>
		///   Create a new injector
		/// </summary>
		public Injector()
		{
			_writer = Record ? new CaptureFileWriterDevice("cap.pcap") : null;
			_writer?.Open();
		}

		/// <summary>
		///   Local ip addresses we should ignore
		/// </summary>
		public IPAddress[]? Addresses { get; set; }

		/// <summary>
		///   Represents a physical link to the outside world
		/// </summary>
		public ILiveDevice? InternetDevice
		{
			get => _internetDevice;
			set
			{
				if (_internetDevice != null)
				{
					Console.WriteLine($@"Stopping capture on {_internetDevice.Description}");
					_internetDevice.StopCapture();
					_internetDevice.Close();
				}

				_internetDevice = value;

				if (_internetDevice == null)
				{
					return;
				}

				_internetDevice.Open(new DeviceConfiguration
					{ Snaplen = 65000, Mode = DeviceModes.MaxResponsiveness, BufferSize = 65000 * 100, ReadTimeout = 100 });
				_internetDevice.OnPacketArrival += InternetDeviceOnOnPacketArrival;
				_internetDevice.StartCapture();
				Console.WriteLine($@"Starting capture on {_internetDevice.Description}.");
			}
		}

		/// <summary>
		///   Represents the link where we will inject packets to
		/// </summary>
		public ILiveDevice? InternalDevice
		{
			get => _internalDevice;
			set
			{
				if (_internalDevice != null)
				{
					Console.WriteLine($@"Closing device {_internalDevice.Description}");
					//_internalDevice.StopCapture();
					_internalDevice.Close();
				}

				_internalDevice = value;
				if (_internalDevice == null)
				{
					return;
				}

				_internalDevice.Open(new DeviceConfiguration
					{ Snaplen = 65000, Mode = DeviceModes.MaxResponsiveness, BufferSize = 65000 * 100, ReadTimeout = 100 });
				//_internalDevice.OnPacketArrival += InternalDeviceOnOnPacketArrival;
				//_internalDevice.StartCapture();
				Console.WriteLine($@"{_internalDevice.Description} is ready for sending!");
			}
		}

		/// <summary>
		///   MAC address to rewrite packets for
		/// </summary>
		public PhysicalAddress? RewriteDestination { get; set; }

		/// <summary>
		///   Whether this injector is enabled or not
		/// </summary>
		public bool Enabled { get; set; }

		/// <summary>
		///   The number of captured packets
		/// </summary>
		public ulong CapturedPackets { get; private set; }

		/// <summary>
		///   The number of handled packets
		/// </summary>
		public ulong HandledPackets { get; private set; }

		/// <summary>
		///   Whether we are ready or not
		/// </summary>
		public bool Ready => _internalDevice != null && _internetDevice != null && RewriteDestination != null;

		/// <summary>
		///   Injects a packet and updates counters
		/// </summary>
		/// <param name="packet">The packet to inject</param>
		private void AnnouncePacket(EthernetPacket packet)
		{
			packet.DestinationHardwareAddress = RewriteDestination;

			try
			{
				HandledPackets += 1;
				InternalDevice?.SendPacket(packet);
				_writer?.SendPacket(packet);
			}
			catch (Exception ex)
			{
				Console.WriteLine($@"Failed to write packet: {ex.Message}");
			}
		}

		/// <summary>
		///   Receives a packet and determines if we should rewrite it
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void InternetDeviceOnOnPacketArrival(object sender, PacketCapture e)
		{
			if (!Enabled)
			{
				return;
			}

			CapturedPackets += 1;

			//_captured += 1;

			// parse the packet
			var rawPacket = e.GetPacket();
			var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

			// check that the packet is an ethernet packet with our mac address
			if (packet is not EthernetPacket eth)
			{
				return;
			}

			if (eth.Type != EthernetType.IPv6 || !Equals(eth.DestinationHardwareAddress, _internalDevice?.MacAddress))
			{
				return;
			}

			var icmp = packet.Extract<IcmpV6Packet>();
			if (icmp != null)
			{
				// check if packet is a regular ping
				// otherwise we don't want to break the network...
				if (icmp.Type is not (IcmpV6Type.EchoReply or IcmpV6Type.EchoRequest))
				{
					return;
				}

				Console.WriteLine($@"Going to rewrite ping packet to {RewriteDestination}");
				AnnouncePacket(eth);
				return;
			}

			var ipv6 = packet.Extract<IPv6Packet>();
			if (ipv6 != null)
			{
				if (Addresses?.Contains(ipv6.DestinationAddress) == true)
				{
					// skip any packets destined for the host ip address.
					return;
				}

				Console.WriteLine($@"Going to rewrite ipv6 packet for {ipv6.DestinationAddress} to {RewriteDestination}");
				AnnouncePacket(eth);
				return;
			}

			var ipv4 = packet.Extract<IPv4Packet>();
			if (ipv4 != null)
			{
				if (Addresses?.Contains(ipv4.DestinationAddress) == true)
				{
					// skip any packets destined for the host ip address
					return;
				}

				Console.WriteLine($@"Going to rewrite ipv4 packet for {ipv4.DestinationAddress} to {RewriteDestination}");
				AnnouncePacket(eth);
			}
		}
	}
}
