using System.Text.Json.Nodes;

namespace Hardened.Amz.Canaries.Runtime.Models.Dashboards;

public record DashboardRequest(
    string Page,
    JsonNode? Data
);
