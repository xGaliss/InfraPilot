namespace InfraPilot.Web.Pages.Agents;

using System.Text.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;
using InfraPilot.Contracts.FileTree;
using InfraPilot.Contracts.Iis;
using InfraPilot.Contracts.ScheduledTasks;
using InfraPilot.Contracts.Services;
using InfraPilot.Contracts.UsersAndGroups;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public sealed class DetailsModel : PageModel
{
    public const string OverviewSection = "overview";
    public const string ServicesSection = "services";
    public const string TasksSection = "tasks";
    public const string IisSection = "iis";
    public const string FileTreeSection = "fileTree";
    public const string UsersAndGroupsSection = "usersAndGroups";
    public const string ActionsSection = "actions";

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

    public string SelectedSection { get; private set; } = OverviewSection;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusTone { get; set; }

    public ServiceSnapshotDto ServicesSnapshot { get; private set; } = new();

    public ScheduledTaskSnapshotDto ScheduledTasksSnapshot { get; private set; } = new();

    public IisSnapshotDto IisSnapshot { get; private set; } = new();

    public FileTreeSnapshotDto FileTreeSnapshot { get; private set; } = new();

    public UsersAndGroupsSnapshotDto UsersAndGroupsSnapshot { get; private set; } = new();

    public IReadOnlyList<CapabilitySnapshotHistoryItemDto> CapabilityHistory { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, string? section, CancellationToken cancellationToken)
    {
        SelectedSection = NormalizeSection(section);
        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid agentId, string? section, CancellationToken cancellationToken)
    {
        await _centralApiClient.ApproveAgentAsync(agentId, cancellationToken);
        StatusTone = "success";
        StatusMessage = "Agent approved. It can now publish snapshots and receive actions.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(agentId, section));
    }

    public async Task<IActionResult> OnPostCancelActionAsync(
        Guid agentId,
        Guid actionId,
        string? section,
        CancellationToken cancellationToken)
    {
        try
        {
            var action = await _centralApiClient.CancelActionAsync(
                actionId,
                new ActionCommandCancelRequestDto("WebUI", "Cancelled from the agent workspace."),
                cancellationToken);

            StatusTone = "success";
            StatusMessage = $"Cancelled {action.ActionKey} on {action.TargetKey ?? action.CapabilityKey}.";
        }
        catch (HttpRequestException exception)
        {
            StatusTone = "error";
            StatusMessage = $"The action could not be cancelled. {exception.Message}";
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(agentId, section));
    }

    public async Task<IActionResult> OnPostQueueActionAsync(
        Guid agentId,
        string capabilityKey,
        string actionKey,
        string? targetKey,
        string? section,
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

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(agentId, section));
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

                case CapabilityKeys.UsersAndGroups:
                    UsersAndGroupsSnapshot = JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(capability.LatestPayloadJson, SnapshotJsonOptions)
                        ?? new UsersAndGroupsSnapshotDto();
                    break;
            }
        }

        var selectedCapabilityKey = MapSectionToCapabilityKey(SelectedSection);
        if (selectedCapabilityKey is not null)
        {
            CapabilityHistory = await _centralApiClient.GetCapabilityHistoryAsync(Agent.AgentId, selectedCapabilityKey, 12, cancellationToken);
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

    private static object BuildRouteValues(Guid agentId, string? section)
    {
        var normalizedSection = NormalizeSection(section);
        return new
        {
            id = agentId,
            section = string.Equals(normalizedSection, OverviewSection, StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedSection
        };
    }

    private static string NormalizeSection(string? section)
        => section switch
        {
            ServicesSection => ServicesSection,
            TasksSection => TasksSection,
            IisSection => IisSection,
            FileTreeSection => FileTreeSection,
            UsersAndGroupsSection => UsersAndGroupsSection,
            ActionsSection => ActionsSection,
            _ => OverviewSection
        };

    public static string? MapSectionToCapabilityKey(string section)
        => section switch
        {
            ServicesSection => CapabilityKeys.Services,
            TasksSection => CapabilityKeys.ScheduledTasks,
            IisSection => CapabilityKeys.Iis,
            FileTreeSection => CapabilityKeys.FileTree,
            UsersAndGroupsSection => CapabilityKeys.UsersAndGroups,
            _ => null
        };

    public static string? MapCapabilityKeyToSection(string capabilityKey)
        => capabilityKey switch
        {
            CapabilityKeys.Services => ServicesSection,
            CapabilityKeys.ScheduledTasks => TasksSection,
            CapabilityKeys.Iis => IisSection,
            CapabilityKeys.FileTree => FileTreeSection,
            CapabilityKeys.UsersAndGroups => UsersAndGroupsSection,
            _ => null
        };
}
