namespace SmartX.API.Models
{
    public class ActionRecordDto
    {
        public string? Kind { get; set; }
        public string? Target { get; set; }
        public string? Command { get; set; }
    }

    public class SuggestedActionDto
    {
        public string Kind { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string? Command { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public int Occurrences { get; set; }
    }

    public class InsightDto
    {
        public string Severity { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
    }

    public class InsightsResponseDto
    {
        public List<SuggestedActionDto> SuggestedActions { get; set; } = new();
        public List<InsightDto> Insights { get; set; } = new();
    }
}