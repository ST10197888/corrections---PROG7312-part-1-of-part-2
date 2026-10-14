namespace SmartX.API.Models
{
    public class SensorDto
    {
        public int Id { get; set; }
        public string? MacAddress { get; set; }
        public string? LocationZone { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public DateTime RegisteredDate { get; set; }
        public bool IsActive { get; set; }
    }
}