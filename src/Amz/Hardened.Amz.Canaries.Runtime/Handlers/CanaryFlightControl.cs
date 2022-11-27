using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Requests.Abstract.Attributes;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Canaries.Runtime.Handlers;

public class CanaryFlightControl
{
    private ICanaryAirTrafficControlService _airTrafficControlService;
    private ILogger<CanaryFlightControl> _logger;

    public CanaryFlightControl(
        ICanaryAirTrafficControlService airTrafficControlService, ILogger<CanaryFlightControl> logger)
    {
        _airTrafficControlService = airTrafficControlService;
        _logger = logger;
    }

    [HardenedFunction("canary-flight-controller")]
    public async Task<FlightControllerResponseModel> FlightController()
    {
        try
        {
            await _airTrafficControlService.ScheduleFlights();

            return new FlightControllerResponseModel(true);
        }
        catch (Exception exp)
        {
            _logger.LogError(exp, "Exception thrown while scheduling canaries");

            return new FlightControllerResponseModel(false);
        }
    }
}