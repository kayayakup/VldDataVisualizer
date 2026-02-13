using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VldDataVisualizer.Models;
using VldDataVisualizer.ViewModels;

namespace VldDataVisualizer.Services
{
    public class TfprPollingService
    {
        private readonly TfprVldModbusTcpReader _reader;
        private CancellationTokenSource _cts;

        public event EventHandler<VldData> DataReceived;
        public string dataa;

        public TfprPollingService(TfprVldModbusTcpReader reader)
        {
            _reader = reader;
        }

        public void Start(TfprVldModbusTcpReader _tcpreader, int intervalMs = 500)
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => PollLoop(intervalMs, _cts.Token, _tcpreader));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task PollLoop(int interval, CancellationToken token, TfprVldModbusTcpReader _tcpReader)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = _reader.Read();
                    DataReceived?.Invoke(this, data);
                }
                catch
                {
                    DataReceived?.Invoke(this, new VldData
                    {
                        IsCommunicationActive = false,
                        Status = "COMM_LOST"
                    });
                }
                dataa = "Current: " + _tcpReader.Read().Current.ToString() + "/" +
            "Voltage: " + _tcpReader.Read().DcVoltage.ToString() + "/" +
            "Status: " + _tcpReader.Read().Status.ToString() + "/" +
            "Device ID: " + _tcpReader.Read().DeviceId.ToString() + "/";
                await Task.Delay(interval, token);
            }
        }
    }
}
