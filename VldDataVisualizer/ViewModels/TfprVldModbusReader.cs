using System;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using VldDataVisualizer.Models;

namespace VldDataVisualizer.ViewModels
{
    /// <summary>
    /// Weintek MT8071iE HMI için Modbus TCP Reader
    /// LW (Local Word) Mapping:
    /// LW-100 : DC Voltage (0.1V birimi)
    /// LW-101 : Ground Current (0.01A birimi)
    /// LW-102 : Voltage In (0.1V birimi)
    /// LW-103 : Current (0.1A birimi)
    /// LW-104 : Frequency (0.01Hz birimi)
    /// LW-105 : Temperature (0.1°C birimi)
    /// LW-110 : Status Word (bit bazında durum)
    /// 
    /// NOT: Modbus register adresleri 0-based'dir.
    /// LW-100 = Modbus register 0x0064 (100 decimal)
    /// </summary>
    public class TfprVldModbusTcpReader : IDisposable
    {
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId = 1;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        private const byte UNIT_ID = 0x01;          // Modbus slave ID
        private const byte FC_READ_HOLDING = 0x03;  // Function Code 3: Read Holding Registers
        private const byte FC_ERROR_FLAG = 0x80;    // Function Code error flag

        // === LW BASE ADDRESSES (0-based Modbus) ===
        private const ushort LW_MEASURE_BASE = 0x0064; // LW-100 = decimal 100
        private const ushort LW_STATUS_WORD = 0x006E;  // LW-110 = decimal 110

        // Scaling factors (Weintek'te genellikle 0.1 faktörü kullanılır)
        private const double VOLTAGE_SCALE = 0.1;
        private const double CURRENT_SCALE = 0.01;
        private const double FREQUENCY_SCALE = 0.01;
        private const double TEMPERATURE_SCALE = 0.1;

        // Alarm thresholds
        private const double TOUCH_VOLTAGE_ALARM = 120.0;    // Volt
        private const double GROUND_CURRENT_ALARM = 10.0;    // Amper
        private const double TEMPERATURE_ALARM = 80.0;       // Celsius

        // Connection settings
        private const int DEFAULT_PORT = 502;      // Modbus TCP standart port
        private const int CONNECT_TIMEOUT = 3000;  // 3 saniye
        private const int IO_TIMEOUT = 2000;       // 2 saniye

        public TfprVldModbusTcpReader(string ipAddress, int port = DEFAULT_PORT)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP adresi boş olamaz.", nameof(ipAddress));

            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Port 1-65535 arasında olmalıdır.");

            _client = new TcpClient
            {
                ReceiveTimeout = IO_TIMEOUT,
                SendTimeout = IO_TIMEOUT
            };

            try
            {
                // Async connect with timeout
                var connectTask = _client.ConnectAsync(ipAddress, port);
                if (!connectTask.Wait(CONNECT_TIMEOUT))
                {
                    _client.Close();
                    throw new TimeoutException($"Bağlantı zaman aşımı: {ipAddress}:{port}");
                }

                if (!_client.Connected)
                {
                    throw new SocketException((int)SocketError.ConnectionRefused);
                }

                _stream = _client.GetStream();
            }
            catch (Exception ex) when (!(ex is SocketException || ex is TimeoutException))
            {
                throw new InvalidOperationException($"Bağlantı hatası: {ex.Message}", ex);
            }
        }

        #region Public Methods

        /// <summary>
        /// VLD verilerini oku
        /// </summary>
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
                    // === BLOCK READ : LW-100 – LW-105 (6 register) ===
                    ushort[] measurementRegs = ReadHoldingRegisters(LW_MEASURE_BASE, 6);

                    // Apply scaling factors
                    data.DcVoltage = Math.Round(measurementRegs[0] * VOLTAGE_SCALE, 3); // LW-100
                    data.GroundCurrent = Math.Round(measurementRegs[1] * CURRENT_SCALE, 3); // LW-101
                    data.VoltageIn = Math.Round(measurementRegs[2] * VOLTAGE_SCALE, 3);       // LW-102
                    data.Current = Math.Round(measurementRegs[3] * CURRENT_SCALE, 3); // LW-103
                    data.Frequency = Math.Round(measurementRegs[4] * FREQUENCY_SCALE, 3);     // LW-104
                    data.Temperature = Math.Round(measurementRegs[5] * TEMPERATURE_SCALE, 3); // LW-105

                    // === STATUS WORD : LW-110 ===
                    ushort[] statusReg = ReadHoldingRegisters(LW_STATUS_WORD, 1);
                    data.DeviceStatusWord = statusReg[0];

                    // === DERIVED VALUES ===
                    data.TouchVoltage = Math.Round(data.GroundCurrent * 1000.0, 1);
                    data.DcCurrent = 2.5f;
                    data.DcPower = Math.Round(data.DcVoltage * data.DcCurrent, 1);

                    // Validate data
                    ValidateMeasurements(data);

                    // Evaluate alarms
                    EvaluateStatus(data);
                }
                catch (Exception ex) when (ex is SocketException || ex is TimeoutException || ex is InvalidOperationException)
                {
                    data.IsCommunicationActive = false;
                    data.Status = "COMM_LOST";

                    // Optionally reconnect
                    // TryReconnect();
                }
                catch (Exception ex)
                {
                    data.IsCommunicationActive = false;
                    data.Status = "ERROR";
                }

                return data;
            }
        }

        /// <summary>
        /// Bağlantı durumunu kontrol et
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_disposed || _client == null)
                    return false;

                try
                {
                    // Quick socket check
                    if (_client.Connected && (_client.Client.Poll(1000, SelectMode.SelectRead)))
                    {
                        MessageBox.Show("Connected");
                        return (_client.Client.Available == 0);
                    }
                    else
                    {
                        return false;
                    }

                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Bağlantıyı yeniden dene
        /// </summary>
        public bool TryReconnect()
        {
            lock (_syncLock)
            {
                if (_disposed) return false;

                try
                {
                    DisposeResources();

                    // Yeniden bağlanma mantığı buraya eklenebilir
                    // Not: TcpClient yeniden bağlanmayı desteklemez, yeni instance gerekir
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion

        #region Modbus TCP Core

        private ushort[] ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            if (count < 1 || count > 125) // Modbus TCP max 125 register
                throw new ArgumentOutOfRangeException(nameof(count), "Register sayısı 1-125 arasında olmalıdır.");

            byte[] request = BuildReadRequest(startAddress, count);

            lock (_syncLock)
            {
                _stream.Write(request, 0, request.Length);

                // MBAP Header (7 bytes) + FC(1) + ByteCount(1) + Data(2*count)
                int expectedResponseLength = 9 + (count * 2);
                byte[] response = ReadExact(expectedResponseLength);

                // Validate response
                ValidateResponse(request, response, count);

                // Parse registers
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

            // MBAP Header
            ushort transactionId = _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)(transactionId & 0xFF);
            frame[2] = 0x00; // Protocol ID High
            frame[3] = 0x00; // Protocol ID Low
            frame[4] = 0x00; // Length High
            frame[5] = 0x06; // Length Low (6 bytes to follow)
            frame[6] = UNIT_ID;

            // PDU
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

            // Check transaction ID
            ushort reqTransId = (ushort)((request[0] << 8) | request[1]);
            ushort respTransId = (ushort)((response[0] << 8) | response[1]);

            if (reqTransId != respTransId)
                throw new InvalidOperationException("Transaction ID uyuşmuyor");

            // Check unit ID
            if (response[6] != UNIT_ID)
                throw new InvalidOperationException($"Unit ID uyuşmuyor: {response[6]}");

            // Check for error response
            if ((response[7] & FC_ERROR_FLAG) == FC_ERROR_FLAG)
            {
                byte errorCode = response[8];
                string errorMsg = GetModbusErrorDescription(errorCode);
                throw new InvalidOperationException($"Modbus hatası: {errorMsg} (Code: {errorCode})");
            }

            // Check function code
            if (response[7] != FC_READ_HOLDING)
                throw new InvalidOperationException($"Beklenen fonksiyon kodu: {FC_READ_HOLDING}, alınan: {response[7]}");

            // Check byte count
            int expectedByteCount = expectedRegisterCount * 2;
            if (response[8] != expectedByteCount)
                throw new InvalidOperationException($"Byte sayısı uyuşmuyor: {response[8]}, beklenen: {expectedByteCount}");
        }

        private string GetModbusErrorDescription(byte errorCode)
        {
            return errorCode switch
            {
                0x01 => "Illegal Function",
                0x02 => "Illegal Data Address",
                0x03 => "Illegal Data Value",
                0x04 => "Slave Device Failure",
                0x05 => "Acknowledge",
                0x06 => "Slave Device Busy",
                0x07 => "Negative Acknowledge",
                0x08 => "Memory Parity Error",
                0x0A => "Gateway Path Unavailable",
                0x0B => "Gateway Target Device Failed to Respond",
                _ => $"Unknown Error ({errorCode:X2})"
            };
        }

        #endregion

        #region Data Validation & Status

        private void ValidateMeasurements(VldData data)
        {
            // Sınır değer kontrolleri
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

            // Bit bazında status word analizi
            AnalyzeStatusWord(data);

            // Threshold alarmları
            if (data.TouchVoltage > TOUCH_VOLTAGE_ALARM)
                data.ActiveAlarms.Add($"DOKUNMA_GERILIMI_YUKSEK ({data.TouchVoltage:F1}V)");

            if (data.GroundCurrent > GROUND_CURRENT_ALARM)
                data.ActiveAlarms.Add($"TOPRAK_ARIZASI ({data.GroundCurrent:F2}A)");

            if (data.Temperature > TEMPERATURE_ALARM)
                data.ActiveAlarms.Add($"ASIRI_SICAKLIK ({data.Temperature:F1}°C)");

            // Power quality warnings
            if (data.DcPower > data.DcVoltage * 100) // Örnek: Maksimum güç sınırı
                data.ActiveAlarms.Add($"ASIRI_GUC ({data.DcPower:F1}W)");

            data.Status = data.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";

        }

        private void AnalyzeStatusWord(VldData data)
        {
            // Status word bit analizi (örnek)
            // Bit 0: Communication status
            // Bit 1: Over voltage
            // Bit 2: Under voltage
            // Bit 3: Over current
            // ... vb.

            ushort status = data.DeviceStatusWord;

            if ((status & 0x0001) == 0)
                data.ActiveAlarms.Add("COMM_FAULT");

            if ((status & 0x0002) != 0)
                data.ActiveAlarms.Add("OVER_VOLTAGE");

            if ((status & 0x0004) != 0)
                data.ActiveAlarms.Add("UNDER_VOLTAGE");

            if ((status & 0x0008) != 0)
                data.ActiveAlarms.Add("OVER_CURRENT");
        }

        #endregion

        #region IDisposable Implementation

        private void DisposeResources()
        {
            try
            {
                _stream?.Close();
                _stream?.Dispose();
            }
            catch { }

            try
            {
                _client?.Close();
                _client?.Dispose();
            }
            catch { }
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
                    {
                        DisposeResources();
                    }
                }

                _disposed = true;
            }
        }

        ~TfprVldModbusTcpReader()
        {
            Dispose(false);
        }

        #endregion
    }
}