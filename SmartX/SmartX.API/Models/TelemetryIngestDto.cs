namespace SmartX.API.Models
{
    public class TelemetryIngestDto
    {
        public string? MacAddress { get; set; }
        public string? LocationZone { get; set; }
        public string? Category { get; set; }
        public dynamic? SensorValue { get; set; }
        public DateTime Timestamp { get; set; }
    }
}