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

        private const byte UNIT_ID = 0x01;
        private const byte FC_READ_HOLDING = 0x03;
        private const byte FC_ERROR_FLAG = 0x80;

        private const ushort LW_MEASURE_BASE = 0x0064; // LW-100
        private const ushort LW_STATUS_WORD = 0x006E;  // LW-110

        private const double VOLTAGE_SCALE = 0.1;
        private const double CURRENT_SCALE = 0.01;
        private const double FREQUENCY_SCALE = 0.01;
        private const double TEMPERATURE_SCALE = 0.1;

        private const double TOUCH_VOLTAGE_ALARM = 120.0;
        private const double GROUND_CURRENT_ALARM = 10.0;
        private const double TEMPERATURE_ALARM = 80.0;

        private const int DEFAULT_PORT = 502;
        private const int CONNECT_TIMEOUT = 3000;
        private const int IO_TIMEOUT = 2000;

        public TfprVldModbusTcpReader(string ipAddress, int port = DEFAULT_PORT)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP adresi boş olamaz.", nameof(ipAddress));

            _client = new TcpClient
            {
                ReceiveTimeout = IO_TIMEOUT,
                SendTimeout = IO_TIMEOUT
            };

            var connectTask = _client.ConnectAsync(ipAddress, port);
            if (!connectTask.Wait(CONNECT_TIMEOUT))
            {
                _client.Close();
                throw new TimeoutException($"Bağlantı zaman aşımı: {ipAddress}:{port}");
            }

            _stream = _client.GetStream();
        }

        public VldData Read()
        {
            lock (_syncLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TfprVldModbusTcpReader));

                var data = new VldData
                {
                    Timestamp = DateTime.Now,
                    DeviceType = "TFPR-VLD",
                    IsCommunicationActive = true
                };

                try
                {
                    // === BLOCK READ LW-100 – LW-105 ===
                    ushort[] measurementRegs = ReadHoldingRegisters(LW_MEASURE_BASE, 6);

                    // Raw registerleri konsola yazdır
                    for (int i = 0; i < measurementRegs.Length; i++)
                        Debug.WriteLine($"LW-{100 + i} raw: {measurementRegs[i]}");

                    // Scaling uygulanarak gerçek ölçüm değerleri
                    data.DcVoltage = Math.Round(measurementRegs[0] * VOLTAGE_SCALE, 3);   // LW-100
                    data.GroundCurrent = Math.Round(measurementRegs[1] * CURRENT_SCALE, 3); // LW-101
                    data.VoltageIn = Math.Round(measurementRegs[2] * VOLTAGE_SCALE, 3);    // LW-102
                    data.Current = Math.Round(measurementRegs[3] * CURRENT_SCALE, 3);      // LW-103
                    data.Frequency = Math.Round(measurementRegs[4] * FREQUENCY_SCALE, 3);  // LW-104
                    data.Temperature = Math.Round(measurementRegs[5] * TEMPERATURE_SCALE, 3); // LW-105

                    // === STATUS WORD ===
                    ushort[] statusReg = ReadHoldingRegisters(LW_STATUS_WORD, 1);
                    data.DeviceStatusWord = statusReg[0];
                    Debug.WriteLine($"Status Word raw: 0x{data.DeviceStatusWord:X4}");

                    // === DERIVED VALUES ===
                    data.TouchVoltage = Math.Round(data.GroundCurrent * 1000.0, 1);
                    data.DcPower = Math.Round(data.DcVoltage * data.Current, 1); // DC Power gerçek current ile

                    // Validate data
                    ValidateMeasurements(data);

                    // Evaluate alarms
                    EvaluateStatus(data);
                }
                catch
                {
                    data.IsCommunicationActive = false;
                    data.Status = "COMM_LOST";
                }

                return data;
            }
        }

        private ushort[] ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            byte[] request = BuildReadRequest(startAddress, count);
            lock (_syncLock)
            {
                _stream.Write(request, 0, request.Length);
                int expectedResponseLength = 9 + (count * 2);
                byte[] response = ReadExact(expectedResponseLength);

                ValidateResponse(request, response, count);

                ushort[] registers = new ushort[count];
                int dataIndex = 9; // MBAP(7) + FC(1) + ByteCount(1)
                for (int i = 0; i < count; i++)
                {
                    registers[i] = (ushort)((response[dataIndex] << 8) | response[dataIndex + 1]);
                    dataIndex += 2;
                }
                return registers;
            }
        }

        private byte[] BuildReadRequest(ushort address, ushort count)
        {
            byte[] frame = new byte[12];
            ushort transactionId = _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)(transactionId & 0xFF);
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

            return frame;
        }

        private byte[] ReadExact(int length)
        {
            byte[] buffer = new byte[length];
            int totalRead = 0;
            DateTime startTime = DateTime.Now;

            while (totalRead < length)
            {
                if ((DateTime.Now - startTime).TotalMilliseconds > IO_TIMEOUT)
                    throw new TimeoutException($"Okuma zaman aşımı: {totalRead}/{length} bytes okundu");

                int bytesRead = _stream.Read(buffer, totalRead, length - totalRead);
                if (bytesRead == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                totalRead += bytesRead;
            }

            return buffer;
        }

        private void ValidateResponse(byte[] request, byte[] response, ushort expectedRegisterCount)
        {
            if (response == null || response.Length < 9)
                throw new InvalidOperationException("Geçersiz Modbus yanıtı");

            ushort reqTransId = (ushort)((request[0] << 8) | request[1]);
            ushort respTransId = (ushort)((response[0] << 8) | response[1]);

            if (reqTransId != respTransId)
                throw new InvalidOperationException("Transaction ID uyuşmuyor");

            if (response[6] != UNIT_ID)
                throw new InvalidOperationException($"Unit ID uyuşmuyor: {response[6]}");

            if ((response[7] & FC_ERROR_FLAG) == FC_ERROR_FLAG)
                throw new InvalidOperationException($"Modbus hatası: Code {response[8]}");

            if (response[7] != FC_READ_HOLDING)
                throw new InvalidOperationException($"Beklenen fonksiyon kodu: {FC_READ_HOLDING}, alınan: {response[7]}");

            int expectedByteCount = expectedRegisterCount * 2;
            if (response[8] != expectedByteCount)
                throw new InvalidOperationException($"Byte sayısı uyuşmuyor: {response[8]}, beklenen: {expectedByteCount}");
        }

        private void ValidateMeasurements(VldData data)
        {
            if (data.DcVoltage < 0 || data.DcVoltage > 1000)
                throw new InvalidOperationException($"Geçersiz DC gerilim: {data.DcVoltage}V");

            if (data.GroundCurrent < 0 || data.GroundCurrent > 100)
                throw new InvalidOperationException($"Geçersiz toprak akımı: {data.GroundCurrent}A");

            if (data.Temperature < -40 || data.Temperature > 150)
                throw new InvalidOperationException($"Geçersiz sıcaklık: {data.Temperature}°C");

            if (data.Frequency < 45 || data.Frequency > 65)
                data.Status = "FREQ_WARN";
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

            if (data.TouchVoltage > TOUCH_VOLTAGE_ALARM)
                data.ActiveAlarms.Add($"DOKUNMA_GERILIMI_YUKSEK ({data.TouchVoltage:F1}V)");

            if (data.GroundCurrent > GROUND_CURRENT_ALARM)
                data.ActiveAlarms.Add($"TOPRAK_ARIZASI ({data.GroundCurrent:F2}A)");

            if (data.Temperature > TEMPERATURE_ALARM)
                data.ActiveAlarms.Add($"ASIRI_SICAKLIK ({data.Temperature:F1}°C)");

            if (data.DcPower > data.DcVoltage * 100)
                data.ActiveAlarms.Add($"ASIRI_GUC ({data.DcPower:F1}W)");

            data.Status = data.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";
        }

        private void DisposeResources()
        {
            _stream?.Close();
            _stream?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_syncLock)
                        DisposeResources();
                }
                _disposed = true;
            }
        }

        ~TfprVldModbusTcpReader()
        {
            Dispose(false);
        }
    }
}
