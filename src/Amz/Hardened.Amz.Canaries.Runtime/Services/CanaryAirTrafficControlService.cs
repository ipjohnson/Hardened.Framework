using Hardened.Amz.Canaries.Runtime.Discovery;
using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface ICanaryAirTrafficControlService
{
    Task ScheduleFlights();
}

[Expose]
[Singleton]
public class CanaryAirTrafficControlService : ICanaryAirTrafficControlService
{
    private ICanaryDiscoveryService _canaryDiscoveryService;
    private ICanaryDynamoReader _dynamoReader;
    private ICanaryDynamoWriter _dynamoWriter;
    private ISqsFlightScheduler _sqsFlightScheduler;
    
    public CanaryAirTrafficControlService(
        ICanaryDiscoveryService canaryDiscoveryService, 
        ICanaryDynamoReader dynamoReader,
        ICanaryDynamoWriter dynamoWriter, 
        ISqsFlightScheduler sqsFlightScheduler)
    {
        _canaryDiscoveryService = canaryDiscoveryService;
        _dynamoReader = dynamoReader;
        _dynamoWriter = dynamoWriter;
        _sqsFlightScheduler = sqsFlightScheduler;
    }

    public async Task ScheduleFlights()
    {
        var currentState = await _dynamoReader.GetCanaryState();

        if (currentState == null)
        {
            await ScaffoldStateDocument();
            
            await ScheduleAllFlights();
        }
        else
        {
            await InspectCanariesForNextFlight(currentState);
        }
    }

    private Task ScaffoldStateDocument()
    {
        return _dynamoWriter.WriteInitialStateDocument(
            _canaryDiscoveryService.CanaryDefinitions
            );
    }

    private async Task InspectCanariesForNextFlight(CurrentCanaryState currentCanaryState)
    {
        foreach (KeyValuePair<string, CanaryDefinition> canaryKvp in _canaryDiscoveryService.CanaryDefinitions)
        {
            var historyList = await _dynamoReader.GetCanaryHistory(canaryKvp.Key);
            
            if (historyList == null ||
                ShouldScheduleFlight(canaryKvp, historyList))
            {
                await ScheduleCanary(canaryKvp);
            }
        }
    }

    private async Task ScheduleAllFlights()
    {
        foreach (KeyValuePair<string,CanaryDefinition> canaryDefinition in _canaryDiscoveryService.CanaryDefinitions)
        {
            await ScheduleCanary(canaryDefinition);
        }
    }

    private Task ScheduleCanary(KeyValuePair<string, CanaryDefinition> canaryDefinition)
    {
        return _sqsFlightScheduler.ScheduleFlight(canaryDefinition.Key, canaryDefinition.Value);
    }

    private bool ShouldScheduleFlight(KeyValuePair<string, CanaryDefinition> canaryKvp, CanaryRecentFlightHistory history)
    {
        var lastFight = 
            history.RecentFlights.MaxBy(f => f.FlightTakeOff);

        if (lastFight == null)
        {
            return true;
        }
        
        if (!FrequencyIsOverDue(canaryKvp, lastFight))
        {
            return false;
        }
        
        if (canaryKvp.Value.Frequency.FlightStyle == CanaryFlightStyle.Strict)
        {
            return StrictFlightRules(canaryKvp, lastFight);
        }

        return LooseFlightRules(canaryKvp, lastFight);
    }

    private bool FrequencyIsOverDue(KeyValuePair<string,CanaryDefinition> canaryKvp, CanaryFlightInfo lastFight)
    {
        return lastFight.FlightTakeOff.Add(canaryKvp.Value.Frequency.Duration) <= DateTime.Now;
    }

    private bool LooseFlightRules(KeyValuePair<string,CanaryDefinition> canaryKvp, CanaryFlightInfo lastFight)
    {
        if (lastFight.FlightTime.HasValue)
        {
            return true;
        }

        if (lastFight.FlightTakeOff.Add(new TimeSpan(0, 15, 0)) <= DateTime.Now)
        {
            return true;
        }

        return false;
    }

    private bool StrictFlightRules(KeyValuePair<string,CanaryDefinition> canaryKvp, CanaryFlightInfo lastFight)
    {
        return true;
    }
}