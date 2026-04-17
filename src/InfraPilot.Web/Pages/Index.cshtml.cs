namespace InfraPilot.Web.Pages;

using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Changes;
using InfraPilot.Contracts.Common;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class IndexModel : PageModel
{
    private readonly CentralApiClient _centralApiClient;

    public IndexModel(CentralApiClient centralApiClient)
    {
        _centralApiClient = centralApiClient;
    }

    public string? Search { get; private set; }

    public string? Health { get; private set; }

    public string? Status { get; private set; }

    public IReadOnlyList<AgentListItemDto> Agents { get; private set; } = [];

    public IReadOnlyList<AgentListItemDto> FilteredAgents { get; private set; } = [];

    public IReadOnlyList<CapabilityChangeEventDto> RecentChanges { get; private set; } = [];

    public int HealthyCount => Agents.Count(agent => agent.HealthStatus == AgentHealthStatuses.Healthy);

    public int DelayedCount => Agents.Count(agent => agent.HealthStatus == AgentHealthStatuses.Delayed);

    public int OfflineCount => Agents.Count(agent => agent.HealthStatus == AgentHealthStatuses.Offline);

    public int NeedsApprovalCount => Agents.Count(agent => agent.HealthStatus == AgentHealthStatuses.NeedsApproval);

    public int ActiveActionCount => Agents.Sum(agent => agent.PendingActionCount + agent.InProgressActionCount);

    public string GetHealthClass(string healthStatus)
        => healthStatus switch
        {
            AgentHealthStatuses.Healthy => "health-good",
            AgentHealthStatuses.Delayed => "health-warn",
            AgentHealthStatuses.Offline => "health-bad",
            AgentHealthStatuses.NeedsApproval => "health-neutral",
            AgentHealthStatuses.Revoked => "health-bad",
            _ => "health-neutral"
        };

    public async Task OnGetAsync(string? search, string? health, string? status, CancellationToken cancellationToken)
    {
        var agentsTask = _centralApiClient.GetAgentsAsync(cancellationToken);
        var changesTask = _centralApiClient.GetChangeFeedAsync(null, 12, cancellationToken);

        await Task.WhenAll(agentsTask, changesTask);

        Agents = await agentsTask;
        RecentChanges = await changesTask;
        Search = search?.Trim();
        Health = health?.Trim();
        Status = status?.Trim();

        IEnumerable<AgentListItemDto> filtered = Agents;

        if (!string.IsNullOrWhiteSpace(Search))
        {
            filtered = filtered.Where(agent =>
                agent.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || agent.MachineName.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || agent.InstallationId.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || agent.CapabilityKeys.Any(capability => capability.Contains(Search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(Health))
        {
            filtered = filtered.Where(agent => string.Equals(agent.HealthStatus, Health, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(Status))
        {
            filtered = filtered.Where(agent => string.Equals(agent.Status, Status, StringComparison.OrdinalIgnoreCase));
        }

        FilteredAgents = filtered
            .OrderByDescending(agent => agent.HealthStatus == AgentHealthStatuses.Healthy)
            .ThenByDescending(agent => agent.LastSeenUtc)
            .ThenBy(agent => agent.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
