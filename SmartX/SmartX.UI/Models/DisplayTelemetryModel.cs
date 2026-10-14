using System;

namespace SmartX.UI.Models
{
    public class DisplayTelemetryModel
    {
        public string? MacAddress { get; set; }
        public string? LocationZone { get; set; }
        public string? Category { get; set; }
        public dynamic? SensorValue { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Source { get; set; }
    }
}