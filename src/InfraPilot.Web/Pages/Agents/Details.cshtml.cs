namespace InfraPilot.Web.Pages.Agents;

using System.Text.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
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

    public IReadOnlyList<CapabilityHistoryEntryViewModel> CapabilityHistoryEntries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, string? section, CancellationToken cancellationToken)
    {
        SelectedSection = NormalizeSection(section);
        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid agentId, string? section, CancellationToken cancellationToken)
    {
        var result = await _centralApiClient.ApproveAgentAsync(
            agentId,
            BuildControlRequest("Approved from the agent workspace."),
            cancellationToken);
        StatusTone = "success";
        StatusMessage = result.Message;
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(agentId, section));
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid agentId, string? section, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _centralApiClient.RevokeAgentAsync(
                agentId,
                BuildControlRequest("Revoked from the agent workspace."),
                cancellationToken);

            StatusTone = "success";
            StatusMessage = result.Message;
        }
        catch (HttpRequestException exception)
        {
            StatusTone = "error";
            StatusMessage = $"The agent could not be revoked. {exception.Message}";
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(agentId, section));
    }

    public async Task<IActionResult> OnPostResetTokenAsync(Guid agentId, string? section, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _centralApiClient.ResetAgentTokenAsync(
                agentId,
                BuildControlRequest("Token rotated from the agent workspace."),
                cancellationToken);

            StatusTone = "success";
            StatusMessage = result.Message;
        }
        catch (HttpRequestException exception)
        {
            StatusTone = "error";
            StatusMessage = $"The token could not be rotated. {exception.Message}";
        }

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
            CapabilityHistoryEntries = BuildCapabilityHistoryEntries(selectedCapabilityKey, CapabilityHistory);
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

    private static IReadOnlyList<CapabilityHistoryEntryViewModel> BuildCapabilityHistoryEntries(
        string capabilityKey,
        IReadOnlyList<CapabilitySnapshotHistoryItemDto> history)
        => history
            .Where(item => item.ChangeSummary.HasChanges)
            .Select(item => new CapabilityHistoryEntryViewModel(
                item,
                BuildHistoryDiffSections(capabilityKey, item)))
            .ToList();

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildHistoryDiffSections(
        string capabilityKey,
        CapabilitySnapshotHistoryItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.PreviousPayloadJson))
        {
            return [];
        }

        return capabilityKey switch
        {
            CapabilityKeys.Services => BuildServiceHistorySections(item),
            CapabilityKeys.ScheduledTasks => BuildScheduledTaskHistorySections(item),
            CapabilityKeys.Iis => BuildIisHistorySections(item),
            CapabilityKeys.FileTree => BuildFileTreeHistorySections(item),
            CapabilityKeys.UsersAndGroups => BuildUsersAndGroupsHistorySections(item),
            _ => []
        };
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildServiceHistorySections(CapabilitySnapshotHistoryItemDto item)
    {
        var current = JsonSerializer.Deserialize<ServiceSnapshotDto>(item.PayloadJson, SnapshotJsonOptions) ?? new ServiceSnapshotDto();
        var previous = JsonSerializer.Deserialize<ServiceSnapshotDto>(item.PreviousPayloadJson!, SnapshotJsonOptions) ?? new ServiceSnapshotDto();

        var currentMap = current.Services.ToDictionary(service => service.ServiceName, StringComparer.OrdinalIgnoreCase);
        var previousMap = previous.Services.ToDictionary(service => service.ServiceName, StringComparer.OrdinalIgnoreCase);

        return BuildSections(
            CreateSection("Added", currentMap.Keys.Except(previousMap.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .Select(key => $"{currentMap[key].DisplayName} ({key})")
                .ToList()),
            CreateSection("Removed", previousMap.Keys.Except(currentMap.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .Select(key => $"{previousMap[key].DisplayName} ({key})")
                .ToList()),
            CreateSection("State changes", currentMap.Keys.Intersect(previousMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => !string.Equals(currentMap[key].Status, previousMap[key].Status, StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key)
                .Select(key => $"{currentMap[key].DisplayName}: {previousMap[key].Status} -> {currentMap[key].Status}")
                .ToList()));
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildScheduledTaskHistorySections(CapabilitySnapshotHistoryItemDto item)
    {
        var current = JsonSerializer.Deserialize<ScheduledTaskSnapshotDto>(item.PayloadJson, SnapshotJsonOptions) ?? new ScheduledTaskSnapshotDto();
        var previous = JsonSerializer.Deserialize<ScheduledTaskSnapshotDto>(item.PreviousPayloadJson!, SnapshotJsonOptions) ?? new ScheduledTaskSnapshotDto();

        var currentMap = current.Tasks.ToDictionary(task => $"{task.TaskPath}{task.TaskName}", StringComparer.OrdinalIgnoreCase);
        var previousMap = previous.Tasks.ToDictionary(task => $"{task.TaskPath}{task.TaskName}", StringComparer.OrdinalIgnoreCase);

        return BuildSections(
            CreateSection("Added", currentMap.Keys.Except(previousMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Removed", previousMap.Keys.Except(currentMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Changed", currentMap.Keys.Intersect(previousMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                    !string.Equals(currentMap[key].Status, previousMap[key].Status, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentMap[key].TaskToRun, previousMap[key].TaskToRun, StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key)
                .Select(key =>
                {
                    var previousTask = previousMap[key];
                    var currentTask = currentMap[key];
                    return $"{key}: status {previousTask.Status} -> {currentTask.Status}, command {previousTask.TaskToRun ?? "-"} -> {currentTask.TaskToRun ?? "-"}";
                })
                .ToList()));
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildIisHistorySections(CapabilitySnapshotHistoryItemDto item)
    {
        var current = JsonSerializer.Deserialize<IisSnapshotDto>(item.PayloadJson, SnapshotJsonOptions) ?? new IisSnapshotDto();
        var previous = JsonSerializer.Deserialize<IisSnapshotDto>(item.PreviousPayloadJson!, SnapshotJsonOptions) ?? new IisSnapshotDto();

        var currentPools = current.AppPools.ToDictionary(pool => pool.Name, StringComparer.OrdinalIgnoreCase);
        var previousPools = previous.AppPools.ToDictionary(pool => pool.Name, StringComparer.OrdinalIgnoreCase);
        var currentSites = current.Sites.ToDictionary(site => site.Name, StringComparer.OrdinalIgnoreCase);
        var previousSites = previous.Sites.ToDictionary(site => site.Name, StringComparer.OrdinalIgnoreCase);

        return BuildSections(
            CreateSection("App pools added", currentPools.Keys.Except(previousPools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("App pools removed", previousPools.Keys.Except(currentPools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("App pool state changes", currentPools.Keys.Intersect(previousPools.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => !string.Equals(currentPools[key].State, previousPools[key].State, StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key)
                .Select(key => $"{key}: {previousPools[key].State} -> {currentPools[key].State}")
                .ToList()),
            CreateSection("Sites added", currentSites.Keys.Except(previousSites.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Sites removed", previousSites.Keys.Except(currentSites.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Site changes", currentSites.Keys.Intersect(previousSites.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                    !string.Equals(currentSites[key].State, previousSites[key].State, StringComparison.OrdinalIgnoreCase) ||
                    !AreSetsEqual(currentSites[key].Bindings, previousSites[key].Bindings))
                .OrderBy(key => key)
                .Select(key =>
                {
                    var bindingNote = AreSetsEqual(currentSites[key].Bindings, previousSites[key].Bindings)
                        ? string.Empty
                        : $" bindings {string.Join(", ", previousSites[key].Bindings)} -> {string.Join(", ", currentSites[key].Bindings)}";
                    return $"{key}: {previousSites[key].State} -> {currentSites[key].State}{bindingNote}";
                })
                .ToList()));
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildFileTreeHistorySections(CapabilitySnapshotHistoryItemDto item)
    {
        var current = JsonSerializer.Deserialize<FileTreeSnapshotDto>(item.PayloadJson, SnapshotJsonOptions) ?? new FileTreeSnapshotDto();
        var previous = JsonSerializer.Deserialize<FileTreeSnapshotDto>(item.PreviousPayloadJson!, SnapshotJsonOptions) ?? new FileTreeSnapshotDto();

        var currentRoots = current.Roots.ToDictionary(root => root.RootPath, StringComparer.OrdinalIgnoreCase);
        var previousRoots = previous.Roots.ToDictionary(root => root.RootPath, StringComparer.OrdinalIgnoreCase);
        var currentNodes = FlattenNodes(current.Roots);
        var previousNodes = FlattenNodes(previous.Roots);

        return BuildSections(
            CreateSection("Roots added", currentRoots.Keys.Except(previousRoots.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Roots removed", previousRoots.Keys.Except(currentRoots.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Paths added", currentNodes.Keys.Except(previousNodes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Paths removed", previousNodes.Keys.Except(currentNodes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Path changes", currentNodes.Keys.Intersect(previousNodes.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                    currentNodes[key].IsDirectory != previousNodes[key].IsDirectory ||
                    currentNodes[key].SizeBytes != previousNodes[key].SizeBytes ||
                    !string.Equals(currentNodes[key].Owner, previousNodes[key].Owner, StringComparison.OrdinalIgnoreCase) ||
                    !AreSetsEqual(currentNodes[key].Permissions, previousNodes[key].Permissions))
                .OrderBy(key => key)
                .Select(key =>
                {
                    var notes = new List<string>();
                    if (currentNodes[key].SizeBytes != previousNodes[key].SizeBytes)
                    {
                        notes.Add($"size {previousNodes[key].SizeBytes?.ToString() ?? "-"} -> {currentNodes[key].SizeBytes?.ToString() ?? "-"}");
                    }
                    if (!string.Equals(currentNodes[key].Owner, previousNodes[key].Owner, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add($"owner {previousNodes[key].Owner ?? "-"} -> {currentNodes[key].Owner ?? "-"}");
                    }
                    if (!AreSetsEqual(currentNodes[key].Permissions, previousNodes[key].Permissions))
                    {
                        notes.Add("permissions changed");
                    }

                    return $"{key}: {string.Join(", ", notes)}";
                })
                .ToList()));
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildUsersAndGroupsHistorySections(CapabilitySnapshotHistoryItemDto item)
    {
        var current = JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(item.PayloadJson, SnapshotJsonOptions) ?? new UsersAndGroupsSnapshotDto();
        var previous = JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(item.PreviousPayloadJson!, SnapshotJsonOptions) ?? new UsersAndGroupsSnapshotDto();

        var currentUsers = current.Users.ToDictionary(user => user.Name, StringComparer.OrdinalIgnoreCase);
        var previousUsers = previous.Users.ToDictionary(user => user.Name, StringComparer.OrdinalIgnoreCase);
        var currentGroups = current.Groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
        var previousGroups = previous.Groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);

        return BuildSections(
            CreateSection("Users added", currentUsers.Keys.Except(previousUsers.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Users removed", previousUsers.Keys.Except(currentUsers.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("User state changes", currentUsers.Keys.Intersect(previousUsers.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => currentUsers[key].Enabled != previousUsers[key].Enabled)
                .OrderBy(key => key)
                .Select(key => $"{key}: {(previousUsers[key].Enabled ? "Enabled" : "Disabled")} -> {(currentUsers[key].Enabled ? "Enabled" : "Disabled")}")
                .ToList()),
            CreateSection("Groups added", currentGroups.Keys.Except(previousGroups.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Groups removed", previousGroups.Keys.Except(currentGroups.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList()),
            CreateSection("Membership changes", currentGroups.Keys.Intersect(previousGroups.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => !AreSetsEqual(
                    currentGroups[key].Members.Select(member => member.Name),
                    previousGroups[key].Members.Select(member => member.Name)))
                .OrderBy(key => key)
                .Select(key =>
                {
                    var currentMembers = new HashSet<string>(currentGroups[key].Members.Select(member => member.Name), StringComparer.OrdinalIgnoreCase);
                    var previousMembers = new HashSet<string>(previousGroups[key].Members.Select(member => member.Name), StringComparer.OrdinalIgnoreCase);
                    var added = currentMembers.Except(previousMembers, StringComparer.OrdinalIgnoreCase).ToList();
                    var removed = previousMembers.Except(currentMembers, StringComparer.OrdinalIgnoreCase).ToList();
                    return $"{key}: +[{string.Join(", ", added)}] -[{string.Join(", ", removed)}]";
                })
                .ToList()));
    }

    private static IReadOnlyList<HistoryDiffSectionViewModel> BuildSections(params HistoryDiffSectionViewModel?[] sections)
        => sections.Where(section => section is not null).Cast<HistoryDiffSectionViewModel>().ToList();

    private static HistoryDiffSectionViewModel? CreateSection(string title, IReadOnlyList<string> lines)
        => lines.Count == 0 ? null : new HistoryDiffSectionViewModel(title, lines);

    private static Dictionary<string, FileTreeNodeDto> FlattenNodes(IReadOnlyList<FileTreeRootDto> roots)
    {
        var nodes = new Dictionary<string, FileTreeNodeDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var node in root.Nodes)
            {
                FlattenNode(nodes, node);
            }
        }

        return nodes;
    }

    private static void FlattenNode(IDictionary<string, FileTreeNodeDto> nodes, FileTreeNodeDto node)
    {
        nodes[node.FullPath] = node;
        foreach (var child in node.Children)
        {
            FlattenNode(nodes, child);
        }
    }

    private static bool AreSetsEqual(IEnumerable<string>? left, IEnumerable<string>? right)
        => new HashSet<string>(left ?? [], StringComparer.OrdinalIgnoreCase).SetEquals(right ?? []);

    private AgentControlRequestDto BuildControlRequest(string reason)
        => new(User.Identity?.Name ?? "WebUI", reason);

    public sealed record CapabilityHistoryEntryViewModel(
        CapabilitySnapshotHistoryItemDto Item,
        IReadOnlyList<HistoryDiffSectionViewModel> Sections);

    public sealed record HistoryDiffSectionViewModel(
        string Title,
        IReadOnlyList<string> Lines);
}
