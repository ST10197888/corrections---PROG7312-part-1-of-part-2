namespace SmartX.API.Models
{
    public class QueueStatsDto
    {
        public int StandardQueueDepth { get; set; }
        public int CriticalQueueDepth { get; set; }
        public long TotalIngested { get; set; }
        public long TotalProcessed { get; set; }
        public long CriticalProcessed { get; set; }
        public long DuplicateAlertsBlocked { get; set; }
        public int ActiveFaultCount { get; set; }
        public int DeviceCount { get; set; }
    }
}