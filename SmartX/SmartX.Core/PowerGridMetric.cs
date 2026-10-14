namespace SmartX.Core
{
    public struct PowerGridMetric
    {
        public int Wattage { get; set; }

        public PowerGridMetric(int wattage)
        {
            Wattage = wattage;
        }

        public static PowerGridMetric operator +(PowerGridMetric a, PowerGridMetric b)
        {
            return new PowerGridMetric(a.Wattage + b.Wattage);
        }

        public static PowerGridMetric operator -(PowerGridMetric a, PowerGridMetric b)
        {
            return new PowerGridMetric(a.Wattage - b.Wattage);
        }

        public static bool operator >(PowerGridMetric a, PowerGridMetric b)
        {
            return a.Wattage > b.Wattage;
        }

        public static bool operator <(PowerGridMetric a, PowerGridMetric b)
        {
            return a.Wattage < b.Wattage;
        }

        public static bool operator ==(PowerGridMetric a, PowerGridMetric b)
        {
            return a.Wattage == b.Wattage;
        }

        public static bool operator !=(PowerGridMetric a, PowerGridMetric b)
        {
            return a.Wattage != b.Wattage;
        }

        public override bool Equals(object obj)
        {
            if (obj is PowerGridMetric metric)
                return Wattage == metric.Wattage;
            return false;
        }

        public override int GetHashCode()
        {
            return Wattage.GetHashCode();
        }
    }
}
