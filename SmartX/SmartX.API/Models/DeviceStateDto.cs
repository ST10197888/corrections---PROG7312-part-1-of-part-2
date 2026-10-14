namespace SmartX.API.Models
{
    public class DeviceStateDto
    {
        public string MacAddress { get; set; } = string.Empty;
        public string? LocationZone { get; set; }
        public string? Category { get; set; }
        public object? LastValue { get; set; }
        public string? SeverityLevel { get; set; }
        public string Status { get; set; } = "Online";
        public DateTime LastSeen { get; set; }
        public int PacketCount { get; set; }
        public string? LastCommand { get; set; }

        public DeviceStateDto Clone() => (DeviceStateDto)MemberwiseClone();
    }
}