using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using VldDataVisualizer.Models;

namespace VldDataVisualizer.ViewModels
{
    /// <summary>
    /// ATS (Otomatik Tren Denetimi) Sisteminden gerçek sinyalizasyon verilerini (tren konumları, hızları, vb.)
    /// Modbus TCP protokolü üzerinden okuyan okuyucu sınıf.
    /// Tıpkı VLD ve YBS'de olduğu gibi, gerçek veriler bu sınıf aracılığıyla çekilir.
    /// </summary>
    public class AtsSignalizationModbusReader : IDisposable
    {
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId = 1;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        private const byte UNIT_ID = 0xFF;
        private const byte FC_READ_HOLDING_REGISTERS = 0x03;

        // ===== ATS Sinyalizasyon Modbus Register Adresleri (Temsili Harita) =====
        // Sistemin genel durumu
        private const ushort REG_SYSTEM_STATUS = 0x0100;    // 0=Normal, 1=Alarm/Hata
        private const ushort REG_ACTIVE_TRAINS = 0x0101;    // Aktif tren sayısı (N)
        
        // Tren verilerinin başlangıç adresi ve her tren için ayrılan register sayısı
        private const ushort REG_TRAINS_START = 0x0110;
        private const ushort REGS_PER_TRAIN = 10;           // Her tren için 10 register okuyacağız

        // Tren blok (TrackBlock) verilerinin başlangıcı
        private const ushort REG_BLOCKS_START = 0x0500;
        private const ushort BLOCKS_COUNT = 30;             // Örnek olarak 30 blok durumu okuyalım (Dolu/Boş)

        private const int DEFAULT_PORT = 502;
        private const int CONNECT_TIMEOUT = 3000;
        private const int IO_TIMEOUT = 2000;

        public AtsSignalizationModbusReader(string ipAddress, int port = DEFAULT_PORT)
        {
            _client = new TcpClient
            {
                ReceiveTimeout = IO_TIMEOUT,
                SendTimeout = IO_TIMEOUT
            };

            var connectTask = _client.ConnectAsync(ipAddress, port);
            if (!connectTask.Wait(CONNECT_TIMEOUT))
                throw new TimeoutException("ATS Sinyalizasyon Modbus bağlantı timeout");

            _stream = _client.GetStream();
        }

        /// <summary>
        /// Modbus üzerinden güncel sinyalizasyon verisini (trenler, bloklar) okur ve SignalizationData nesnesi döndürür.
        /// </summary>
        public SignalizationData ReadData()
        {
            lock (_syncLock)
            {
                var data = new SignalizationData
                {
                    Timestamp = DateTime.Now,
                    SystemId = "ATS_SIGNALIZATION_REAL",
                    SystemType = "CBTC-ATS-MODBUS",
                    IsCommunicationActive = true,
                    SystemStatus = "NORMAL"
                };

                try
                {
                    // 1. Genel Durum ve Aktif Tren Sayısını Oku (2 Register: 0x0100 - 0x0101)
                    ushort[] headerRegs = ReadHoldingRegisters(REG_SYSTEM_STATUS, 2);
                    ushort sysStatus = headerRegs[0];
                    ushort activeTrainCount = headerRegs[1];

                    if (sysStatus != 0)
                    {
                        data.SystemStatus = "ALARM";
                        data.SystemAlarms.Add("ATS Genel Sistem Hatası (Modbus)");
                    }

                    // Güvenlik: Maksimum okunabilir tren sayısını sınırla (Örn: 20)
                    if (activeTrainCount > 20) activeTrainCount = 20;

                    // 2. Aktif Trenlerin Detaylarını Oku
                    if (activeTrainCount > 0)
                    {
                        ushort totalTrainRegsToRead = (ushort)(activeTrainCount * REGS_PER_TRAIN);
                        ushort[] trainRegs = ReadHoldingRegisters(REG_TRAINS_START, totalTrainRegsToRead);

                        for (int i = 0; i < activeTrainCount; i++)
                        {
                            int offset = i * REGS_PER_TRAIN;
                            var train = new TrainInfo
                            {
                                TrainId = trainRegs[offset + 0],
                                TrainNumber = trainRegs[offset + 0],
                                TrainName = $"Gerçek Tren {trainRegs[offset + 0]:00}",
                                CurrentPosition = trainRegs[offset + 1] * 10.0, // Örn: Register değeri 10m katsayılı
                                Speed = trainRegs[offset + 2],                  // km/h
                                // Yön (1: UP/NORTHBOUND, 2: DOWN/SOUTHBOUND)
                                Direction = trainRegs[offset + 3] == 1 ? "NORTHBOUND" : "SOUTHBOUND",
                                TrackType = trainRegs[offset + 3] == 1 ? "UP" : "DOWN",
                                // Durum (0: MOVING, 1: STOPPED, 2: DWELLING)
                                Status = GetTrainStatusString(trainRegs[offset + 4]),
                                PassengerCount = trainRegs[offset + 5],
                                NextStationId = trainRegs[offset + 6],
                                DistanceToNextStation = trainRegs[offset + 7] * 10.0,
                                IsInService = true,
                                LastUpdateTime = DateTime.Now
                            };

                            data.ActiveTrains.Add(train);
                        }
                    }

                    // 3. Hat Bloklarının (TrackBlocks) Doluluk Durumlarını Oku (İsteğe bağlı)
                    // Her bir bitin bir bloğu temsil ettiğini varsayalım (30 blok = 2 register yeterli)
                    ushort[] blockRegs = ReadHoldingRegisters(REG_BLOCKS_START, 2);
                    uint blockBits = (uint)((blockRegs[0] << 16) | blockRegs[1]);

                    for (int i = 0; i < BLOCKS_COUNT; i++)
                    {
                        bool isOccupied = (blockBits & (1 << i)) != 0;
                        var block = new TrackBlock
                        {
                            BlockId = i + 1,
                            BlockName = $"Bölge {i + 1}",
                            IsOccupied = isOccupied,
                            Status = isOccupied ? "OCCUPIED" : "FREE"
                        };
                        data.TrackBlocks.Add(block);
                    }

                    Debug.WriteLine($"--- Sinyalizasyon Gerçek Veri Okuma ---");
                    Debug.WriteLine($"  Sistem Durumu: {data.SystemStatus}");
                    Debug.WriteLine($"  Aktif Tren Sayısı: {data.ActiveTrains.Count}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ATS Sinyalizasyon Modbus Error: " + ex.Message);
                    data.IsCommunicationActive = false;
                    data.SystemStatus = "COMM_LOST";
                    data.SystemAlarms.Add("Bağlantı Hatası: " + ex.Message);
                }

                return data;
            }
        }

        private string GetTrainStatusString(ushort statusCode)
        {
            return statusCode switch
            {
                0 => "MOVING",
                1 => "STOPPED",
                2 => "DWELLING (Yolcu Alıyor)",
                3 => "FAULT",
                _ => "UNKNOWN"
            };
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
            frame[7] = FC_READ_HOLDING_REGISTERS;

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

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                _client?.Dispose();
                _disposed = true;
            }
        }
    }
}
