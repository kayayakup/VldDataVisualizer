using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VldDataVisualizer.ViewModels
{
    public class TfprVldRegisterScanner : IDisposable
    {
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId = 1;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        private const byte UNIT_ID = 0xFF;
        private const byte FC_READ = 0x03;
        private const int DEFAULT_PORT = 502;
        private const int CONNECT_TIMEOUT = 3000;
        private const int IO_TIMEOUT = 2000;

        public TfprVldRegisterScanner(string ipAddress, int port = DEFAULT_PORT)
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

        /// <summary>
        /// Belirtilen aralıktaki holding register'larını tarar ve konsola yazdırır.
        /// </summary>
        /// <param name="startAddress">Başlangıç adresi (0 tabanlı)</param>
        /// <param name="endAddress">Bitiş adresi (dahil)</param>
        public void Scan(int startAddress, int endAddress)
        {
            if (startAddress > endAddress)
                throw new ArgumentException("Başlangıç adresi bitiş adresinden büyük olamaz.");

            Console.WriteLine($"TFPR-VLD Register Tarama Başladı: {startAddress} - {endAddress}");
            Console.WriteLine("Adres\tHam(hex)\tUInt16\tInt16\tFloat (4-byte)");
            Console.WriteLine("--------------------------------------------------------------");

            const int maxRegistersPerRequest = 125; // Modbus max holding register okuma limiti
            int current = startAddress;

            while (current <= endAddress)
            {
                int count = Math.Min(maxRegistersPerRequest, endAddress - current + 1);
                ushort[] registers;

                try
                {
                    registers = ReadHoldingRegisters((ushort)current, (ushort)count);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Hata: Adres {current} okunamadı: {ex.Message}");
                    current += 1; // bir sonraki adrese geç
                    continue;
                }

                // Her bir register'ı teker teker işle
                for (int i = 0; i < registers.Length; i++)
                {
                    int address = current + i;
                    ushort raw = registers[i];

                    // Float yorumu: eğer adres çift ve bir sonraki register da bu blok içinde varsa
                    string floatStr = "N/A";
                    if (address % 2 == 0 && i + 1 < registers.Length)
                    {
                        ushort next = registers[i + 1];
                        float f = ToFloat(raw, next);
                        // Gerçekçi bir değer aralığında mı? (isteğe bağlı filtre)
                        if (!float.IsNaN(f) && !float.IsInfinity(f))
                            floatStr = f.ToString("F3");
                    }

                    Console.WriteLine($"{address}\t0x{raw:X4}\t{raw}\t{(short)raw}\t{floatStr}");
                }

                current += registers.Length;
            }

            Console.WriteLine("Tarama tamamlandı.");
        }

        public ushort[] ReadHoldingRegisters(ushort startAddress, ushort count = 1)
        {
            byte[] request = BuildReadRequest(startAddress, count);

            lock (_syncLock)
            {
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
        }

        private byte[] BuildReadRequest(ushort address, ushort count)
        {
            byte[] frame = new byte[12];

            ushort transId = _transactionId++;
            if (_transactionId == 0) _transactionId = 1;

            frame[0] = (byte)(transId >> 8);
            frame[1] = (byte)(transId & 0xFF);
            // Protocol ID = 0
            frame[4] = 0x00;
            frame[5] = 0x06;   // Length (6 bytes after this)
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

        private float ToFloat(ushort high, ushort low)
        {
            byte[] bytes = new byte[4];
            // TFPR cihazlarında genellikle high word önce gelir (big-endian)
            bytes[0] = (byte)(high >> 8);
            bytes[1] = (byte)(high & 0xFF);
            bytes[2] = (byte)(low >> 8);
            bytes[3] = (byte)(low & 0xFF);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToSingle(bytes, 0);
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
