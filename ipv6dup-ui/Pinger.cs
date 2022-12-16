using System.Diagnostics;

namespace ipv6dup_ui
{
	internal class Pinger
	{
		/// <summary>
		///   Represents the current status
		/// </summary>
		public enum Status
		{
			NotConnected,
			Connected,
			Degraded
		}

		/// <summary>
		///   The IP address to ping
		/// </summary>
		private readonly string _ipAddress;

		/// <summary>
		///   The running ping process
		/// </summary>
		private Process _process;

		/// <summary>
		///   Create a new pinger
		/// </summary>
		/// <param name="ipAddress">The ip address to ping</param>
		public Pinger(string ipAddress)
		{
			_ipAddress = ipAddress;
			CurrentStatus = Status.NotConnected;
			_process = Start();
		}

		/// <summary>
		///   The current status
		/// </summary>
		public Status CurrentStatus { get; private set; }

		/// <summary>
		///   The last sequence number processed
		/// </summary>
		public int LastSeq { get; private set; }

		/// <summary>
		///   The sequence number of the last duplicate ping
		/// </summary>
		public int LastDup { get; private set; }

		/// <summary>
		///   The sequence number of the last failure
		/// </summary>
		public int LastFailure { get; private set; }

		/// <summary>
		///   The last sequence number when we updated the status to failure
		/// </summary>
		public int LastSetFailure { get; private set; }

		/// <summary>
		///   The sequence number of the first failure
		/// </summary>
		public int FirstFailure { get; private set; }

		/// <summary>
		///   The sequence number of the last successful ping
		/// </summary>
		public int LastSuccess { get; private set; }

		/// <summary>
		///   The sequence number of the first duplicate ping
		/// </summary>
		public int FirstDup { get; private set; }

		/// <summary>
		///   Whether the ping is currently being "helped" by an injector
		/// </summary>
		public bool Helping { get; set; }

		/// <summary>
		///   Starts the pinger process
		/// </summary>
		/// <returns></returns>
		private Process Start()
		{
			var startInfo = new ProcessStartInfo("wsl.exe", $"ping {_ipAddress} -i 1 -W 2 -O")
			{
				UseShellExecute = false,
				ErrorDialog = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			var cmd = new Process
			{
				StartInfo = startInfo
			};
			cmd.OutputDataReceived += CmdOnOutputDataReceived;
			cmd.Exited += CmdOnExited;
			cmd.Start();
			cmd.BeginOutputReadLine();
			cmd.BeginErrorReadLine();

			return cmd;
		}

		~Pinger()
		{
			_process.Exited -= CmdOnExited;
			_process.Kill(true);
		}

		/// <summary>
		///   Called when the pinger exits which should be unexpected
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CmdOnExited(object? sender, EventArgs e)
		{
			_process = Start();
		}

		/// <summary>
		///   Processes a ping reply and updates the status as needed
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CmdOnOutputDataReceived(object sender, DataReceivedEventArgs e)
		{
			if (e.Data == null || e.Data.Contains("56 data bytes"))
			{
				return;
			}

			if (e.Data.StartsWith("no answer yet for icmp_seq"))
			{
				// only consider disconnected if we get 3 in a row
				var currentSeq = int.Parse(e.Data.Split('=')[1].Trim());
				if (currentSeq - LastFailure == 1)
				{
					// this is a consecutive failure, is it the third one?
					if (currentSeq - FirstFailure >= 10)
					{
						CurrentStatus = Status.NotConnected;
						LastSetFailure = currentSeq;
					}
				}
				else
				{
					// not a consecutive failure
					FirstFailure = currentSeq;
				}

				// update statuses
				LastFailure = currentSeq;
				LastSeq = currentSeq;
			}

			if (e.Data.StartsWith("64 bytes"))
			{
				var currentSeq = int.Parse(e.Data.Split('=')[1].Split(" ")[0].Trim());

				if (LastDup != 0)
				{
					// we have seen some dups in the past
					if (currentSeq - LastDup == 1)
					{
						if (currentSeq - FirstDup >= 2)
						{
							// it is safe to say we're connected
							CurrentStatus = Status.Connected;
						}
					}
					else
					{
						FirstDup = currentSeq;
					}
				}
				else if (Helping)
				{
					// we have something helping us get a ping, so we're still degraded
					CurrentStatus = Status.Degraded;
				}
				else
				{
					// we have never seen a dup, so this is easy
					CurrentStatus = Status.Connected;
					LastSuccess = currentSeq;
				}

				if (e.Data.Contains("DUP!"))
				{
					LastDup = currentSeq;
				}

				LastSeq = currentSeq;
			}

			Console.WriteLine($@"{e.Data}: {CurrentStatus}");
		}
	}
}
