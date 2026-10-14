namespace SmartX.API.Models
{
    public class TelemetryHistoryDto
    {
        public string? MacAddress { get; set; }
        public int TotalRecords { get; set; }
        public List<TelemetryResponseDto>? Records { get; set; } = new();
    }
}