using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Shared.Testing.Impl;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Amz.Canaries.Runtime.Attributes;

[XunitTestCaseDiscoverer("Hardened.Shared.Testing.Impl." + nameof(HardenedTestDiscoverer), "Hardened.Shared.Testing")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedCanaryAttribute : FactAttribute
{
    /// <summary>
    /// Name of the canary if empty it will be the method name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Frequency to execute default once per minute
    /// </summary>
    public int Frequency { get; set; } = 1;

    /// <summary>
    /// Frequency unit, defaults to minute
    /// </summary>
    public CanaryFrequencyUnit Unit { get; set; } = CanaryFrequencyUnit.Minute;

    /// <summary>
    /// Report metrics to cloud watch
    /// </summary>
    public bool ReportMetric { get; set; } = true;

    /// <summary>
    /// Flight style for canary
    /// </summary>
    public CanaryFlightStyle FlightStyle { get; set; } = CanaryFlightStyle.Loose;
    
    /// <summary>
    /// By default only one instance of a canary will execute at a time
    /// </summary>
    public bool AllowConcurrentExecution { get; set; } = false;
}