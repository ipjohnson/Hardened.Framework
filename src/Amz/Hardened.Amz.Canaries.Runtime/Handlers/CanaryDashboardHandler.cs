using Hardened.Amz.Canaries.Runtime.Models.Dashboards;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Hardened.Amz.Canaries.Runtime.Handlers;

public class CanaryDashboardHandler
{
    private ITemplateExecutionService _templateExecutionService;
    private IDashboardDataService _dashboardDataService;
    private ILogger<CanaryDashboardHandler> _logger;

    public CanaryDashboardHandler(ITemplateExecutionService templateExecutionService, ILogger<CanaryDashboardHandler> logger, IDashboardDataService dashboardDataService)
    {
        _templateExecutionService = templateExecutionService;
        _logger = logger;
        _dashboardDataService = dashboardDataService;
    }

    [HardenedFunction("canary-cloud-watch-dashboard")]
    public async Task<string> CloudWatchDashboard(JsonNode node, IServiceProvider serviceProvider)
    {
        _logger.LogInformation("{Node}", node);

        var data =
            await _dashboardDataService.GetDashboardData(new DashboardRequest("1", null));
        
        return await _templateExecutionService.Execute("main", data, serviceProvider);
    }
}