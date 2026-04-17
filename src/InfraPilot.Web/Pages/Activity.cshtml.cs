namespace InfraPilot.Web.Pages;

using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Changes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class ActivityModel : PageModel
{
    private readonly CentralApiClient _centralApiClient;

    public ActivityModel(CentralApiClient centralApiClient)
    {
        _centralApiClient = centralApiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? AgentId { get; set; }

    public IReadOnlyList<AgentListItemDto> Agents { get; private set; } = [];

    public IReadOnlyList<CapabilityChangeEventDto> Changes { get; private set; } = [];

    public AgentListItemDto? SelectedAgent => AgentId is null
        ? null
        : Agents.FirstOrDefault(agent => agent.AgentId == AgentId.Value);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var agentsTask = _centralApiClient.GetAgentsAsync(cancellationToken);
        var changesTask = _centralApiClient.GetChangeFeedAsync(AgentId, 80, cancellationToken);

        await Task.WhenAll(agentsTask, changesTask);

        Agents = await agentsTask;
        Changes = await changesTask;
    }
}
