using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VldDataVisualizer.ViewModels
{
    public class TfprVldRegisterDiscovery
    {
        private readonly TfprVldRegisterScanner _scanner;
        private readonly Dictionary<int, List<ushort>> _history = new Dictionary<int, List<ushort>>();
        private readonly object _lock = new object();

        public TfprVldRegisterDiscovery(string ip, int port)
        {
            _scanner = new TfprVldRegisterScanner(ip, port);
        }

        public void CollectSamples(int start, int end, int durationSeconds, int intervalMs)
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => SampleLoop(start, end, intervalMs, cts.Token));
            Thread.Sleep(durationSeconds * 1000);
            cts.Cancel();
            task.Wait();
        }

        private async Task SampleLoop(int start, int end, int interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                for (int addr = start; addr <= end; addr++)
                {
                    try
                    {
                        ushort[] regs = _scanner.ReadHoldingRegisters((ushort)addr);
                        ushort val = regs[0];
                        lock (_lock)
                        {
                            if (!_history.ContainsKey(addr))
                                _history[addr] = new List<ushort>();
                            _history[addr].Add(val);
                            // Limit history size to avoid memory bloat
                            if (_history[addr].Count > 1000)
                                _history[addr].RemoveAt(0);
                        }
                    }
                    catch { }
                }
                await Task.Delay(interval, token);
            }
        }

        public void AnalyzeAndPrintReport()
        {
            Console.WriteLine("=== REGISTER KEŞİF RAPORU ===");
            Console.WriteLine("Adres\tTip\tDeğişim Oranı\tOlası Anlam");
            foreach (var kvp in _history)
            {
                int addr = kvp.Key;
                var values = kvp.Value;
                if (values.Count < 2) continue;

                // Değişim oranı: kaç farklı değer var?
                var distinct = values.Distinct().Count();
                float changeRatio = (float)distinct / values.Count;

                // Ardışık farkların mutlak ortalaması
                double avgDelta = 0;
                for (int i = 1; i < values.Count; i++)
                    avgDelta += Math.Abs(values[i] - values[i - 1]);
                avgDelta /= (values.Count - 1);

                string type = "UINT16";
                string meaning = "";

                // Float olup olmadığını kontrol et (adres çiftse bir sonraki register ile float üret)
                if (addr % 2 == 0 && _history.ContainsKey(addr + 1))
                {
                    var nextValues = _history[addr + 1];
                    if (nextValues.Count >= values.Count)
                    {
                        // Float'a çevrilmiş değerlerin varyansına bak
                        List<float> floats = new List<float>();
                        for (int i = 0; i < values.Count; i++)
                        {
                            float f = ToFloat(values[i], nextValues[i]);
                            if (!float.IsNaN(f)) floats.Add(f);
                        }
                        if (floats.Count > 0)
                        {
                            var floatVariance = floats.Select(f => (double)f).Variance();
                            // Eğer float değerler anlamlı bir aralıkta değişiyorsa, bu bir fiziksel büyüklük olabilir
                            if (floatVariance > 0.01 && floats.Max() - floats.Min() < 10000)
                                type = "FLOAT (pair)";
                        }
                    }
                }

                // Status word olma olasılığı: değişimler genellikle küçük bit değişimleridir
                if (avgDelta < 1 && distinct <= 16)
                    type = "STATUS WORD";

                // Sabit değerler (cihaz bilgisi)
                if (distinct == 1)
                    type = "CONSTANT";

                // Artan/azalan sayıcı olabilir
                if (avgDelta > 0 && Math.Abs(values.Last() - values.First()) > 100 && distinct > 10)
                    type = "COUNTER";

                Console.WriteLine($"{addr}\t{type}\t{changeRatio:P1}\t{meaning}");
            }
        }

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
    }

    // Variance extension metodu
    public static class EnumerableExtensions
    {
        public static double Variance(this IEnumerable<double> source)
        {
            var list = source.ToList();
            if (list.Count == 0) return 0;
            double avg = list.Average();
            return list.Sum(x => (x - avg) * (x - avg)) / list.Count;
        }
    }
}
