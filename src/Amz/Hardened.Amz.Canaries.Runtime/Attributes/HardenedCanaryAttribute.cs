using Hardened.Shared.Testing.Impl;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Amz.Canaries.Runtime.Attributes;

[XunitTestCaseDiscoverer("Hardened.Shared.Testing.Impl." + nameof(HardenedTestDiscoverer), "Hardened.Shared.Testing")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedCanaryAttribute : FactAttribute
{
    /// <summary>
    /// Frequency to execute default once per minute
    /// </summary>
    public int? Frequency { get; set; }

    /// <summary>
    /// Report metrics to cloud watch
    /// </summary>
    public bool? ReportMetric { get; set; }

    /// <summary>
    /// By default only one instance of a canary will execute at a time
    /// </summary>
    public bool ConcurrentExecution { get; set; } = false;
}