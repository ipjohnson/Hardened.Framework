namespace Hardened.Shared.Runtime.Metrics
{
    public class MetricUnits
    {
        public MetricUnits(string name)
        {
            Name = name;
        }

        public string Name { get; }
        
        public static readonly MetricUnits Milliseconds = new("Milliseconds");

        public static readonly MetricUnits Seconds = new("Seconds");

        public static readonly MetricUnits Count = new ("Count");
    }
}
