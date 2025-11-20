namespace VldDataVisualizer.Models
{
    public class BlockInfo
    {
        public int BlockId { get; set; }
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }
        public string CurrentTrain { get; set; }
        public string Status { get; set; }
        public string SignalStatus { get; set; }
    }
}