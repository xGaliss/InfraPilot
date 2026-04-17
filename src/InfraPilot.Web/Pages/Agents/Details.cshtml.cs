namespace InfraPilot.Web.Pages.Agents;

using System.Text.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class DetailsModel : PageModel
{
    private readonly CentralApiClient _centralApiClient;

    public DetailsModel(CentralApiClient centralApiClient)
    {
        _centralApiClient = centralApiClient;
    }

    public AgentDetailDto? Agent { get; private set; }

    public ServiceSnapshotDto? ServicesSnapshot { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await _centralApiClient.ApproveAgentAsync(agentId, cancellationToken);
        return RedirectToPage(new { id = agentId });
    }

    public async Task<IActionResult> OnPostQueueActionAsync(
        Guid agentId,
        string capabilityKey,
        string actionKey,
        string? targetKey,
        CancellationToken cancellationToken)
    {
        await _centralApiClient.CreateActionAsync(
            new ActionCommandCreateRequestDto(
                agentId,
                capabilityKey,
                actionKey,
                targetKey,
                null,
                "WebUI"),
            cancellationToken);

        return RedirectToPage(new { id = agentId });
    }

    private async Task LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Agent = await _centralApiClient.GetAgentDetailAsync(id, cancellationToken);
        if (Agent is null)
        {
            return;
        }

        var servicesCapability = Agent.Capabilities.FirstOrDefault(capability =>
            string.Equals(capability.Descriptor.CapabilityKey, CapabilityKeys.Services, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(servicesCapability?.LatestPayloadJson))
        {
            ServicesSnapshot = JsonSerializer.Deserialize<ServiceSnapshotDto>(servicesCapability.LatestPayloadJson)
                ?? new ServiceSnapshotDto();
        }
    }
}
