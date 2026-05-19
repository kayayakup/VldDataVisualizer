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

        // ===== GERÇEK ADRESLER (Fotoğraftaki tabloya göre) =====
        // Adres 0x06 → Decimal 6: DC Gerilim Trip (bit4), AC Gerilim Trip (bit5), I_RMS Trip (bit6)
        // Adres 0x07 → Decimal 7: Ayirici_Acik (bit0), Ayirici_Kapali (bit1), Seçici Anahtar Toprak Poz (bit4)
        // Adres 0x0F → Decimal 15: Reset (0xAA01)

        // Status/Trip register adresleri
        private const ushort STATUS_TRIP_REG = 0x06;   // DC_Gerilim_Trip(bit4), AC_Gerilim_Trip(bit5), I_RMS_Trip(bit6)
        private const ushort STATUS_SWITCH_REG = 0x07;  // Ayirici_Acik(bit0), Ayirici_Kapali(bit1), Toprak Poz(bit4)
        private const ushort RESET_REG = 0x0F;          // Reset komutu (0xAA01)

        // Bit maskeleri - Adres 0x06
        private const ushort BIT_DC_GERILIM_TRIP = 1 << 4;  // Bit 4
        private const ushort BIT_AC_GERILIM_TRIP = 1 << 5;  // Bit 5
        private const ushort BIT_I_RMS_TRIP = 1 << 6;       // Bit 6

        // Bit maskeleri - Adres 0x07
        private const ushort BIT_AYIRICI_ACIK = 1 << 0;     // Bit 0
        private const ushort BIT_AYIRICI_KAPALI = 1 << 1;   // Bit 1
        private const ushort BIT_TOPRAK_POZ = 1 << 4;       // Bit 4

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
                    StationId = 1,
                    StationName = "Depo",
                    IsCommunicationActive = true
                };

                try
                {
                    // ===== DURUM/TRIP REGISTERLERİ (Adres 0x06 ve 0x07) =====
                    // Her iki adresi tek seferde oku (2 register: 0x06 ve 0x07)
                    ushort[] statusRegs = ReadHoldingRegisters(STATUS_TRIP_REG, 2);

                    ushort tripWord = statusRegs[0];    // Adres 0x06
                    ushort switchWord = statusRegs[1];  // Adres 0x07

                    // Trip durumlarını çöz
                    bool dcGerilimTrip = (tripWord & BIT_DC_GERILIM_TRIP) != 0;
                    bool acGerilimTrip = (tripWord & BIT_AC_GERILIM_TRIP) != 0;
                    bool iRmsTrip = (tripWord & BIT_I_RMS_TRIP) != 0;

                    // Ayırıcı ve anahtar durumlarını çöz
                    bool ayiriciAcik = (switchWord & BIT_AYIRICI_ACIK) != 0;
                    bool ayiriciKapali = (switchWord & BIT_AYIRICI_KAPALI) != 0;
                    bool toprakPozisyonu = (switchWord & BIT_TOPRAK_POZ) != 0;

                    // DeviceStatusWord'e tüm bilgileri birleştir
                    data.DeviceStatusWord = (ushort)(tripWord | (switchWord << 8));

                    Debug.WriteLine($"--- VLD Gerçek Veri Okuma ---");
                    Debug.WriteLine($"Adres 0x06 (Trip): 0x{tripWord:X4}");
                    Debug.WriteLine($"  DC Gerilim Trip: {dcGerilimTrip}");
                    Debug.WriteLine($"  AC Gerilim Trip: {acGerilimTrip}");
                    Debug.WriteLine($"  I RMS Trip: {iRmsTrip}");
                    Debug.WriteLine($"Adres 0x07 (Switch): 0x{switchWord:X4}");
                    Debug.WriteLine($"  Ayırıcı Açık: {ayiriciAcik}");
                    Debug.WriteLine($"  Ayırıcı Kapalı: {ayiriciKapali}");
                    Debug.WriteLine($"  Toprak Pozisyonu: {toprakPozisyonu}");

                    // Alarm ve durum değerlendirmesi
                    EvaluateStatus(data, dcGerilimTrip, acGerilimTrip, iRmsTrip,
                                   ayiriciAcik, ayiriciKapali, toprakPozisyonu);
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

        private void EvaluateStatus(VldData data,
            bool dcGerilimTrip, bool acGerilimTrip, bool iRmsTrip,
            bool ayiriciAcik, bool ayiriciKapali, bool toprakPozisyonu)
        {
            data.ActiveAlarms.Clear();

            // Trip alarmları
            if (dcGerilimTrip)
            {
                data.ActiveAlarms.Add("DC_GERILIM_TRIP");
                data.Status = "ALARM";
            }

            if (acGerilimTrip)
            {
                data.ActiveAlarms.Add("AC_GERILIM_TRIP");
                data.Status = "ALARM";
            }

            if (iRmsTrip)
            {
                data.ActiveAlarms.Add("I_RMS_TRIP");
                data.Status = "ALARM";
            }

            // Ayırıcı durum kontrolleri
            if (ayiriciAcik && ayiriciKapali)
            {
                // İki bit aynı anda aktif olmamalı - çelişkili durum
                data.ActiveAlarms.Add("AYIRICI_DURUM_HATASI");
                data.Status = "ALARM";
            }
            else if (!ayiriciAcik && !ayiriciKapali)
            {
                // Ayırıcı belirsiz durumda
                data.ActiveAlarms.Add("AYIRICI_BELIRSIZ");
            }

            if (toprakPozisyonu)
            {
                data.ActiveAlarms.Add("TOPRAK_POZISYONU_AKTIF");
            }

            // Genel durum değerlendirmesi
            if (data.ActiveAlarms.Count == 0)
            {
                data.Status = "NORMAL";
            }
            else if (data.Status != "ALARM")
            {
                data.Status = "UYARI";
            }

            // Durum bilgilerini VldData'ya yaz
            // Ayırıcı açıksa gerilim 0, kapalıysa nominal değer varsayımı
            if (ayiriciAcik && !ayiriciKapali)
            {
                // Ayırıcı açık: devre kesilmiş
                data.DcVoltage = 0;
                data.DcCurrent = 0;
                data.Current = 0;
            }

            // Trip durumundaysa durum bilgisini güncelle
            if (dcGerilimTrip || acGerilimTrip || iRmsTrip)
            {
                data.DcVoltage = 0;  // Trip durumunda gerilim kesilir
                data.DcCurrent = 0;
                data.Current = 0;
            }
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
