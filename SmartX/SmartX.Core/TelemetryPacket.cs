using System;

namespace SmartX.Core
{
    public class TelemetryPacket<T>
    {
        public string MacAddress { get; set; }
        public string LocationZone { get; set; }
        public string Category { get; set; }
        public DateTime Timestamp { get; set; }
        public T SensorValue { get; set; }

        public TelemetryPacket(string macAddress, string locationZone, string category, T sensorValue)
        {
            MacAddress = macAddress;
            LocationZone = locationZone;
            Category = category;
            Timestamp = DateTime.Now;
            SensorValue = sensorValue;
        }
    }
}
