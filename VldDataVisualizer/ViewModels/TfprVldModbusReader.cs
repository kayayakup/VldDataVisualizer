using System;
using System.Net.Sockets;
using VldDataVisualizer.Models;

namespace VldDataVisualizer.ViewModels
{
    public class TfprVldModbusTcpReader : IDisposable
    {
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId = 1;

        private const byte UNIT_ID = 0x01;
        private const byte FC_READ_HOLDING = 0x03;

        public TfprVldModbusTcpReader(string ipAddress, int port = 502)
        {
            _client = new TcpClient
            {
                ReceiveTimeout = 1500,
                SendTimeout = 1500
            };

            _client.Connect(ipAddress, port);
            _stream = _client.GetStream();
        }

        #region Public Read

        public VldData Read()
        {
            var data = new VldData
            {
                Timestamp = DateTime.Now,
                DeviceType = "TFPR-VLD",
                IsCommunicationActive = true
            };

            try
            {
                // === BLOCK READ (0x0000 – 0x0005) ===
                ushort[] regs = ReadHoldingRegisters(0x0000, 6);

                data.DcVoltage = Math.Round(regs[0] * 0.1, 3);
                data.GroundCurrent = Math.Round(regs[1] * 0.01, 3);
                data.VoltageIn = Math.Round(regs[2] * 0.1, 3);
                data.Current = Math.Round(regs[3] * 0.1, 3);
                data.Frequency = Math.Round(regs[4] * 0.01, 3);
                data.Temperature = Math.Round(regs[5] * 0.1, 3);

                // === STATUS WORD ===
                data.DeviceStatusWord = ReadHoldingRegisters(0x0010, 1)[0];

                // === TÜREV HESAPLAR ===
                data.TouchVoltage = Math.Round(data.GroundCurrent * 1000.0, 1);
                data.DcCurrent = data.Current;
                data.DcPower = Math.Round(data.DcVoltage * data.DcCurrent, 1);

                EvaluateStatus(data);
            }
            catch
            {
                data.IsCommunicationActive = false;
                data.Status = "COMM_LOST";
            }

            return data;
        }

        #endregion

        #region Modbus TCP Core

        private ushort[] ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            byte[] request = BuildReadRequest(startAddress, count);
            _stream.Write(request, 0, request.Length);

            // MBAP(7) + FC(1) + ByteCount(1) + Data(2*count)
            int responseLength = 9 + (count * 2);
            byte[] response = ReadExact(responseLength);

            ushort[] registers = new ushort[count];
            int dataIndex = 9;

            for (int i = 0; i < count; i++)
            {
                registers[i] = (ushort)(response[dataIndex] << 8 | response[dataIndex + 1]);
                dataIndex += 2;
            }

            return registers;
        }

        private byte[] BuildReadRequest(ushort address, ushort count)
        {
            byte[] frame = new byte[12];

            frame[0] = (byte)(_transactionId >> 8);
            frame[1] = (byte)(_transactionId & 0xFF);
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = 0x00;
            frame[5] = 0x06;
            frame[6] = UNIT_ID;

            frame[7] = FC_READ_HOLDING;
            frame[8] = (byte)(address >> 8);
            frame[9] = (byte)(address & 0xFF);
            frame[10] = (byte)(count >> 8);
            frame[11] = (byte)(count & 0xFF);

            _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            return frame;
        }

        private byte[] ReadExact(int length)
        {
            byte[] buffer = new byte[length];
            int read = 0;

            while (read < length)
            {
                int r = _stream.Read(buffer, read, length - read);
                if (r == 0)
                    throw new SocketException();
                read += r;
            }

            return buffer;
        }

        #endregion

        #region Status

        private void EvaluateStatus(VldData data)
        {
            data.ActiveAlarms.Clear();

            if (data.TouchVoltage > 120)
                data.ActiveAlarms.Add($"DOKUNMA_GERILIMI ({data.TouchVoltage}V)");

            if (data.GroundCurrent > 10)
                data.ActiveAlarms.Add($"TOPRAK_ARIZASI ({data.GroundCurrent}A)");

            if (data.Temperature > 80)
                data.ActiveAlarms.Add($"ASIRI_SICAKLIK ({data.Temperature}°C)");

            data.Status = data.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";
        }

        #endregion

        public void Dispose()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
        }
    }
}
