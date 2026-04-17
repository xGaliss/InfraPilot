namespace InfraPilot.Web.Pages.Agents;

using System.Text.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.FileTree;
using InfraPilot.Contracts.Iis;
using InfraPilot.Contracts.ScheduledTasks;
using InfraPilot.Contracts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class DetailsModel : PageModel
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CentralApiClient _centralApiClient;

    public DetailsModel(CentralApiClient centralApiClient)
    {
        _centralApiClient = centralApiClient;
    }

    public AgentDetailDto? Agent { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusTone { get; set; }

    public ServiceSnapshotDto ServicesSnapshot { get; private set; } = new();

    public ScheduledTaskSnapshotDto ScheduledTasksSnapshot { get; private set; } = new();

    public IisSnapshotDto IisSnapshot { get; private set; } = new();

    public FileTreeSnapshotDto FileTreeSnapshot { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid agentId, string? activeTab, CancellationToken cancellationToken)
    {
        await _centralApiClient.ApproveAgentAsync(agentId, cancellationToken);
        StatusTone = "success";
        StatusMessage = "Agent approved. It can now publish snapshots and receive actions.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = agentId }, fragment: activeTab);
    }

    public async Task<IActionResult> OnPostQueueActionAsync(
        Guid agentId,
        string capabilityKey,
        string actionKey,
        string? targetKey,
        string? activeTab,
        CancellationToken cancellationToken)
    {
        try
        {
            var action = await _centralApiClient.CreateActionAsync(
                new ActionCommandCreateRequestDto(
                    agentId,
                    capabilityKey,
                    actionKey,
                    targetKey,
                    null,
                    "WebUI"),
                cancellationToken);

            StatusTone = "success";
            StatusMessage = $"Queued {action.ActionKey} on {action.TargetKey ?? action.CapabilityKey}.";
        }
        catch (HttpRequestException exception)
        {
            StatusTone = "error";
            StatusMessage = $"The action could not be queued. {exception.Message}";
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = agentId }, fragment: activeTab);
    }

    private async Task LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Agent = await _centralApiClient.GetAgentDetailAsync(id, cancellationToken);
        if (Agent is null)
        {
            return;
        }

        foreach (var capability in Agent.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.LatestPayloadJson))
            {
                continue;
            }

            switch (capability.Descriptor.CapabilityKey)
            {
                case CapabilityKeys.Services:
                    ServicesSnapshot = JsonSerializer.Deserialize<ServiceSnapshotDto>(capability.LatestPayloadJson, SnapshotJsonOptions)
                        ?? new ServiceSnapshotDto();
                    break;

                case CapabilityKeys.ScheduledTasks:
                    ScheduledTasksSnapshot = JsonSerializer.Deserialize<ScheduledTaskSnapshotDto>(capability.LatestPayloadJson, SnapshotJsonOptions)
                        ?? new ScheduledTaskSnapshotDto();
                    break;

                case CapabilityKeys.Iis:
                    IisSnapshot = JsonSerializer.Deserialize<IisSnapshotDto>(capability.LatestPayloadJson, SnapshotJsonOptions)
                        ?? new IisSnapshotDto();
                    break;

                case CapabilityKeys.FileTree:
                    FileTreeSnapshot = JsonSerializer.Deserialize<FileTreeSnapshotDto>(capability.LatestPayloadJson, SnapshotJsonOptions)
                        ?? new FileTreeSnapshotDto();
                    break;
            }
        }
    }

    public IReadOnlyList<FileTreeRowViewModel> BuildFileTreeRows(IReadOnlyList<FileTreeNodeDto>? nodes)
    {
        var rows = new List<FileTreeRowViewModel>();
        Flatten(rows, nodes ?? [], 0);
        return rows;
    }

    private static void Flatten(ICollection<FileTreeRowViewModel> rows, IReadOnlyList<FileTreeNodeDto> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            rows.Add(new FileTreeRowViewModel(node.Name, node.FullPath, node.IsDirectory, depth, node.SizeBytes));
            Flatten(rows, node.Children ?? [], depth + 1);
        }
    }

    public sealed record FileTreeRowViewModel(
        string Name,
        string FullPath,
        bool IsDirectory,
        int Depth,
        long? SizeBytes);
}
