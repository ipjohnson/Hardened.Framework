using Hardened.Requests.Abstract.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Handlers;

public class CanaryDashboardHandler
{
    [HardenedFunction("canary-cloud-watch-dashboard")]
    public async Task<string> CloudWatchDashboard()
    {
        return "";
    }
}