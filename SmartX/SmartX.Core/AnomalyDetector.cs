using System.Collections.Generic;
using System.Linq;

namespace SmartX.Core
{
    public static class AnomalyDetector
    {
        private static readonly PowerGridMetric SurgeThreshold =
            new PowerGridMetric(9500);

        private static readonly PowerGridMetric NormalBaseline =
            new PowerGridMetric(5000);

        public static List<TelemetryPacket<object>> DetectPowerSurges(
            List<TelemetryPacket<object>> packets)
        {
            var anomalies = new List<TelemetryPacket<object>>();

            foreach (var packet in packets)
            {
                if (packet.Category != "Power Consumption")
                    continue;

                if (!TryGetWattage(packet.SensorValue, out int wattage))
                    continue;

                var metric = new PowerGridMetric(wattage);

                if (metric > SurgeThreshold)
                    anomalies.Add(packet);
            }

            return anomalies;
        }

        public static PowerGridMetric CalculateAggregateLoad(
            List<PowerGridMetric> meters)
        {
            PowerGridMetric total = new PowerGridMetric(0);

            foreach (var meter in meters)
                total += meter;

            return total;
        }

        public static PowerGridMetric CalculateDelta(
            PowerGridMetric previous,
            PowerGridMetric current)
        {
            return current > previous
                ? current - previous
                : previous - current;
        }

        public static bool IsSystemUnderStress(
            PowerGridMetric currentLoad,
            PowerGridMetric baselineLoad)
        {
            return currentLoad > baselineLoad;
        }

        private static bool TryGetWattage(
            object? value,
            out int wattage)
        {
            switch (value)
            {
                case int integer:
                    wattage = integer;
                    return true;

                case long longValue when longValue >= int.MinValue
                    && longValue <= int.MaxValue:
                    wattage = (int)longValue;
                    return true;

                case double doubleValue when doubleValue >= int.MinValue
                    && doubleValue <= int.MaxValue
                    && doubleValue == System.Math.Truncate(doubleValue):
                    wattage = (int)doubleValue;
                    return true;

                default:
                    wattage = 0;
                    return false;
            }
        }
    }
}
