namespace SmartX.API.Models
{
    public class TelemetryResponseDto
    {
        public int Id { get; set; }
        public string? MacAddress { get; set; }
        public string? LocationZone { get; set; }
        public string? Category { get; set; }
        public dynamic? SensorValue { get; set; }
        public DateTime Timestamp { get; set; }
        public string? SeverityLevel { get; set; }
        public bool IsAnomalous { get; set; }
    }
}