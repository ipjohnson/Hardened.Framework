using Hardened.Amz.Canaries.Runtime.Models.Flight;

namespace Hardened.Amz.Canaries.Runtime.Models.Dashboards;

public record PaginationInfo(int Page, int PageSize, int TotalPages)
{
    public bool FirstPage => Page == 0;

    public bool LastPage => Page == TotalPages - 1;
}

public record DashboardCanaryInstance(
    string CanaryName,
    double? SuccessRate,
    double? Duration,
    CanaryDefinition Definition,
    IReadOnlyList<CanaryFlightInfo> RecentFlights)
{
    public string LastStatus 
    {
        get
        {
            var last = RecentFlights.FirstOrDefault();

            if (last == null)
            {
                return "";
            }

            return Enum.GetName(typeof(FlightStatus), last.FlightStatus)!;
        }
    }

    public string LastRun => 
        RecentFlights.FirstOrDefault()?.FlightTakeOff.ToString("g") ?? "";
}

public record DashboardMainPageResponseModel(
    PaginationInfo Pagination,
    List<DashboardCanaryInstance> Canaries)
{
    
}