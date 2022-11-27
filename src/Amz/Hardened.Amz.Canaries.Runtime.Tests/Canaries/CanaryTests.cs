using Hardened.Amz.Canaries.Runtime.Attributes;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Canaries.Runtime.Tests.Canaries;

public class CanaryTests
{
    
    [HardenedCanary]
    public void DefaultCanary(ILogger<CanaryTests> logger)
    {
        logger.LogInformation("Hello from canary");
    }

    [HardenedCanary(Frequency = 5)]
    public void FrequencyDefaultUnitCanary()
    {
        
    }

    [HardenedCanary(Frequency = 30, Unit = CanaryFrequencyUnit.Second)]
    public void FrequencyUnitCanary()
    {
        
    }

    [HardenedCanary(FlightStyle = CanaryFlightStyle.Strict)]
    public void StrictUnitCanary()
    {
        
    }

    [HardenedCanary(ReportMetric = false)]
    public void ReportMetricFalseCanary()
    {
        
    }

    [HardenedCanary(AllowConcurrentExecution = true)]
    public void AllowConcurrentExecution()
    {
        
    }

    [HardenedCanary(Skip = "test skip")]
    public void SkipCanary()
    {
        
    }
}