using System;
using System.Collections.Generic;
using System.Linq;
using SmartX.Core;

namespace SmartX.UI
{
    public static class AnomalyDetector
    {
        private static readonly PowerGridMetric SURGE_THRESHOLD = new PowerGridMetric(9500);
        private static readonly PowerGridMetric NORMAL_BASELINE = new PowerGridMetric(5000);

        public static List<TelemetryPacket<object>> DetectPowerSurges(List<TelemetryPacket<object>> packets)
        {
            var anomalies = new List<TelemetryPacket<object>>();

            foreach (var packet in packets)
            {
                if (packet.Category == "Power Consumption" && packet.SensorValue is int wattage)
                {
                    var metric = new PowerGridMetric(wattage);
                    
                    if (metric > SURGE_THRESHOLD)
                    {
                        anomalies.Add(packet);
                    }
                }
            }

            return anomalies;
        }

        public static PowerGridMetric CalculateAggregateLoad(List<PowerGridMetric> meters)
        {
            PowerGridMetric total = new PowerGridMetric(0);

            foreach (var meter in meters)
            {
                total = total + meter;
            }

            return total;
        }

        public static PowerGridMetric CalculateDelta(PowerGridMetric previous, PowerGridMetric current)
        {
            if (current > previous)
            {
                return current - previous;
            }
            else
            {
                return previous - current;
            }
        }

        public static bool IsSystemUnderStress(PowerGridMetric currentLoad, PowerGridMetric baselineLoad)
        {
            return currentLoad > baselineLoad;
        }
    }
}
