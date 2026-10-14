using System;
using System.Collections.Generic;

namespace SmartX.UI.Models
{
    public class AlertModel
    {
        public int Id { get; set; }
        public string? SensorMacAddress { get; set; }
        public string? SeverityLevel { get; set; }
        public string? Message { get; set; }
        public dynamic? AnomalousValue { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsResolved { get; set; }
        public string? Notes { get; set; }
        public List<AlertHistoryEntryModel> History { get; set; } = new();
    }
}