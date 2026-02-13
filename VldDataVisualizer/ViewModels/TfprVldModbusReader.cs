using System;
using System.Diagnostics;
using System.Net.Sockets;
using VldDataVisualizer.Models;

namespace VldDataVisualizer.ViewModels
{
    public class TfprVldModbusTcpReader : IDisposable
    {
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId = 1;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        private const byte UNIT_ID = 0xFF;
        private const byte FC_READ = 0x03;

        // Measurement block (FLOAT values)
        private const ushort MEASURE_BASE = 630;
        private const ushort STATUS_REG = 528;

        private const int DEFAULT_PORT = 502;
        private const int CONNECT_TIMEOUT = 3000;
        private const int IO_TIMEOUT = 2000;

        public TfprVldModbusTcpReader(string ipAddress, int port = DEFAULT_PORT)
        {
            _client = new TcpClient
            {
                ReceiveTimeout = IO_TIMEOUT,
                SendTimeout = IO_TIMEOUT
            };

            var connectTask = _client.ConnectAsync(ipAddress, port);
            if (!connectTask.Wait(CONNECT_TIMEOUT))
                throw new TimeoutException("Modbus bağlantı timeout");

            _stream = _client.GetStream();
        }

        public VldData Read()
        {
            lock (_syncLock)
            {
                var data = new VldData
                {
                    Timestamp = DateTime.Now,
                    DeviceType = "TFPR-VLD",
                    IsCommunicationActive = true
                };

                try
                {
                    // ===== FLOAT MEASUREMENT BLOCK =====
                    ushort[] regs = ReadHoldingRegisters(MEASURE_BASE, 8);

                    data.DcVoltage = ToFloat(regs[0], regs[1]);
                    data.GroundCurrent = ToFloat(regs[2], regs[3]);
                    data.Frequency = ToFloat(regs[4], regs[5]);
                    data.Temperature = ToFloat(regs[6], regs[7]);

                    Debug.WriteLine($"DC Voltage = {data.DcVoltage}");
                    Debug.WriteLine($"Ground Current = {data.GroundCurrent}");
                    Debug.WriteLine($"Frequency = {data.Frequency}");
                    Debug.WriteLine($"Temperature = {data.Temperature}");

                    // ===== STATUS =====
                    ushort[] status = ReadHoldingRegisters(STATUS_REG, 1);
                    data.DeviceStatusWord = status[0];

                    EvaluateStatus(data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ERROR: " + ex.Message);
                    data.IsCommunicationActive = false;
                    data.Status = "COMM_LOST";
                }

                return data;
            }
        }

        private ushort[] ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            byte[] request = BuildReadRequest(startAddress, count);

            _stream.Write(request, 0, request.Length);

            int expectedLength = 9 + (count * 2);
            byte[] response = ReadExact(expectedLength);

            ushort[] registers = new ushort[count];

            int index = 9;
            for (int i = 0; i < count; i++)
            {
                registers[i] = (ushort)((response[index] << 8) | response[index + 1]);
                index += 2;
            }

            return registers;
        }

        private byte[] BuildReadRequest(ushort address, ushort count)
        {
            byte[] frame = new byte[12];

            ushort transId = _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            frame[0] = (byte)(transId >> 8);
            frame[1] = (byte)(transId & 0xFF);

            frame[4] = 0x00;
            frame[5] = 0x06;
            frame[6] = UNIT_ID;
            frame[7] = FC_READ;

            frame[8] = (byte)(address >> 8);
            frame[9] = (byte)(address & 0xFF);
            frame[10] = (byte)(count >> 8);
            frame[11] = (byte)(count & 0xFF);

            return frame;
        }

        private byte[] ReadExact(int length)
        {
            byte[] buffer = new byte[length];
            int total = 0;

            while (total < length)
            {
                int read = _stream.Read(buffer, total, length - total);
                if (read == 0)
                    throw new SocketException();

                total += read;
            }

            return buffer;
        }

        // ===== FLOAT CONVERSION =====
        private float ToFloat(ushort reg1, ushort reg2)
        {
            byte[] bytes = new byte[4];

            // Word order test (Most TFPR cihazları bu formatı kullanır)
            bytes[0] = (byte)(reg1 >> 8);
            bytes[1] = (byte)(reg1 & 0xFF);
            bytes[2] = (byte)(reg2 >> 8);
            bytes[3] = (byte)(reg2 & 0xFF);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToSingle(bytes, 0);
        }

        private void EvaluateStatus(VldData data)
        {
            data.ActiveAlarms.Clear();

            ushort status = data.DeviceStatusWord;

            if ((status & 0x0001) == 0)
                data.ActiveAlarms.Add("COMM_FAULT");

            if ((status & 0x0002) != 0)
                data.ActiveAlarms.Add("OVER_VOLTAGE");

            if ((status & 0x0004) != 0)
                data.ActiveAlarms.Add("UNDER_VOLTAGE");

            if ((status & 0x0008) != 0)
                data.ActiveAlarms.Add("OVER_CURRENT");

            data.Status = data.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}
