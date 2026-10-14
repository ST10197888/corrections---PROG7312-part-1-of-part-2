using System;

namespace SmartX.UI.Models
{
    public class SensorModel
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