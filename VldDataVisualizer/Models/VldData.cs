using System.Windows.Controls;
using VldDataVisualizer.ViewModels;

namespace VldDataVisualizer.Models
{
    public class VldData
    {
        // Temel kimlik bilgileri
        public string DeviceId { get; set; } = "VLD_TFPR_001";
        public string DeviceType { get; set; } = "VLD-TFPR";
        public string Location { get; set; } = "TRANSFORMER_STATION";
        public int StationId { get; set; } // Hangi istasyona ait
        public string StationName { get; set; } // İstasyon adı
        public double StartPosition { get; set; } // Kontrol başlangıç pozisyonu (metre)
        public double EndPosition { get; set; } // Kontrol bitiş pozisyonu (metre)
        public DateTime Timestamp { get; set; }

        // Güç parametreleri (dokümanda 36kV'a kadar gerilim mevcut)
        public double VoltageIn { get; set; }           // Giriş gerilimi (kV)
        public double VoltageOut { get; set; }          // Çıkış gerilimi (kV)
        public double Current { get; set; }             // Akım (A)
        public double ActivePower { get; set; }         // Aktif güç (kW)
        public double ReactivePower { get; set; }       // Reaktif güç (kVAr)
        public double ApparentPower { get; set; }       // Görünür güç (kVA)
        public double DcPower { get; set; }       // Görünür güç (kVA)
        public double PowerFactor { get; set; }         // Güç faktörü
        public double TouchVoltage { get; set; } // Kontrol bitiş pozisyonu (metre)

        // Faz bilgileri (dokümanda 3 fazlı sistem)
        public double VoltageL1 { get; set; }
        public double VoltageL2 { get; set; }
        public double VoltageL3 { get; set; }
        public double CurrentL1 { get; set; }
        public double CurrentL2 { get; set; }
        public double CurrentL3 { get; set; }

        // Yeni DC sistem özellikleri
        public double DcVoltage { get; set; }        // kV - 1500 V DC
        public double DcCurrent { get; set; }        // A - DC akım
        public double AuxDcVoltage { get; set; }     // kV - 110 V DC
        public double AuxAcVoltage { get; set; }     // kV - 400 V AC

        // Koruma parametreleri (dokümanda koruma röleleri mevcut)
        public double Temperature { get; set; }         // Sıcaklık (°C)
        public double Frequency { get; set; }           // Frekans (Hz)
        public double GroundCurrent { get; set; }       // Toprak akımı (A)

        // Harmonik analiz (dokümanda güç kalitesi ölçümü)
        public double THDVoltage { get; set; }          // Gerilim THD %
        public double THDCurrent { get; set; }          // Akım THD %

        // Durum ve alarm bilgileri
        public string Status { get; set; } = "NORMAL";
        public List<string> ActiveAlarms { get; set; } = new List<string>();
        public bool IsCommunicationActive { get; set; } = true;

        // Enerji ölçümleri
        public double ActiveEnergyImport { get; set; }  // Tüketilen aktif enerji (kWh)
        public double ActiveEnergyExport { get; set; }  // Üretilen aktif enerji (kWh)
        public double ReactiveEnergyImport { get; set; } // Tüketilen reaktif enerji (kVArh)
        public double ReactiveEnergyExport { get; set; } // Üretilen reaktif enerji (kVArh)

        // Bölge içindeki tren sayısı
        public int TrainsInSection { get; set; } = 0;
    }

    // UI elemanlarına tekrar tekrar ulaşmak için referans tutucu
    public class DevicePanelRefs
    {
        public TextBlock TitleText { get; set; }
        public TextBlock NoDataText { get; set; }
        public Grid MainContentGrid { get; set; }

        // Değer TextBlock'ları (Güncellenecek olanlar)
        public Dictionary<string, TextBlock> ValueTexts { get; set; } = new Dictionary<string, TextBlock>();

        // Grafikler
        public ChartsProperties PowerChart { get; set; }
        public ChartsProperties VoltageChart { get; set; }
        public ChartsProperties CurrentChart { get; set; }
        public ChartsProperties TempChart { get; set; }

        // Kontrol Alanı
        public Canvas ControlCanvas { get; set; }
        public TextBlock ControlAreaHeader { get; set; } // Başlığı güncellemek gerekirse

        // Tablo ve Alarmlar
        public DataGrid DataGrid { get; set; }
        public WrapPanel AlarmPanel { get; set; }
        public GroupBox AlarmGroup { get; set; }
    }
}