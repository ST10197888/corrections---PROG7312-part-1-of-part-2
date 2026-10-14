namespace SmartX.API.Models
{
    public class CommandRequestDto
    {
        public string? MacAddress { get; set; }
        public string? Command { get; set; }
    }

    public class CommandRecordDto
    {
        public int Id { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string? PreviousCommand { get; set; }
        public DateTime IssuedAt { get; set; }
        public bool IsUndone { get; set; }

        public CommandRecordDto Clone() => (CommandRecordDto)MemberwiseClone();
    }
}