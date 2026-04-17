namespace InfraPilot.Web.Pages;

using InfraPilot.Contracts.Agents;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class IndexModel : PageModel
{
    private readonly CentralApiClient _centralApiClient;

    public IndexModel(CentralApiClient centralApiClient)
    {
        _centralApiClient = centralApiClient;
    }

    public IReadOnlyList<AgentListItemDto> Agents { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Agents = await _centralApiClient.GetAgentsAsync(cancellationToken);
    }
}
