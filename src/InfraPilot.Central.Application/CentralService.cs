namespace InfraPilot.Central.Application;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;
using InfraPilot.Contracts.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class CentralService
{
    private readonly ICentralStore _centralStore;
    private readonly CentralOptions _options;
    private readonly ILogger<CentralService> _logger;

    public CentralService(
        ICentralStore centralStore,
        IOptions<CentralOptions> options,
        ILogger<CentralService> logger)
    {
        _centralStore = centralStore;
        _options = options.Value;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
        => _centralStore.InitializeAsync(cancellationToken);

    public async Task<AgentEnrollResponseDto> EnrollAsync(
        string? enrollmentKey,
        AgentEnrollRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(enrollmentKey, _options.EnrollmentKey, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Invalid enrollment key.");
        }

        var existing = await _centralStore.GetAgentByInstallationIdAsync(request.InstallationId, cancellationToken);
        if (existing is not null)
        {
            await _centralStore.UpsertAgentAsync(existing with
            {
                DisplayName = request.DisplayName,
                MachineName = request.MachineName,
                AgentVersion = request.AgentVersion
            }, cancellationToken);

            return new AgentEnrollResponseDto(existing.AgentId, existing.Status, existing.AccessToken, "Agent already enrolled.");
        }

        var agent = new StoredAgent(
            Guid.NewGuid(),
            request.InstallationId,
            request.DisplayName,
            request.MachineName,
            _options.AutoApproveAgents ? AgentStatuses.Approved : AgentStatuses.Pending,
            request.AgentVersion,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            _options.AutoApproveAgents ? DateTimeOffset.UtcNow : null,
            DateTimeOffset.UtcNow);

        await _centralStore.UpsertAgentAsync(agent, cancellationToken);

        _logger.LogInformation(
            "Agent enrolled. AgentId={AgentId} InstallationId={InstallationId} Status={Status}",
            agent.AgentId,
            agent.InstallationId,
            agent.Status);

        return new AgentEnrollResponseDto(agent.AgentId, agent.Status, agent.AccessToken, "Agent enrolled successfully.");
    }

    public async Task RecordHeartbeatAsync(
        string installationId,
        string? token,
        AgentHeartbeatRequestDto request,
        CancellationToken cancellationToken)
    {
        var agent = await GetAuthorizedAgentAsync(installationId, token, cancellationToken);
        await _centralStore.TouchHeartbeatAsync(agent.AgentId, request.AgentVersion, request.SentUtc, cancellationToken);
    }

    public async Task PublishCapabilitiesAsync(
        string installationId,
        string? token,
        IReadOnlyList<CapabilityDescriptorDto> capabilities,
        CancellationToken cancellationToken)
    {
        var agent = await GetAuthorizedAgentAsync(installationId, token, cancellationToken);
        EnsureApproved(agent);
        await _centralStore.ReplaceCapabilitiesAsync(agent.AgentId, capabilities, cancellationToken);
    }

    public async Task PublishSnapshotsAsync(
        string installationId,
        string? token,
        DateTimeOffset collectedUtc,
        IReadOnlyList<CapabilitySnapshotDto> snapshots,
        CancellationToken cancellationToken)
    {
        var agent = await GetAuthorizedAgentAsync(installationId, token, cancellationToken);
        EnsureApproved(agent);
        await _centralStore.AddSnapshotsAsync(agent.AgentId, collectedUtc, snapshots, cancellationToken);
    }

    public async Task<AgentActionCommandDto?> PullNextActionAsync(
        string installationId,
        string? token,
        CancellationToken cancellationToken)
    {
        var agent = await GetAuthorizedAgentAsync(installationId, token, cancellationToken);
        EnsureApproved(agent);
        return await _centralStore.LeaseNextActionAsync(
            agent.AgentId,
            TimeSpan.FromSeconds(Math.Max(15, _options.ActionLeaseSeconds)),
            cancellationToken);
    }

    public async Task ReportActionResultAsync(
        Guid actionId,
        string installationId,
        string? token,
        AgentActionResultReportDto result,
        CancellationToken cancellationToken)
    {
        var agent = await GetAuthorizedAgentAsync(installationId, token, cancellationToken);
        var updated = await _centralStore.CompleteActionAsync(agent.AgentId, actionId, result, cancellationToken);
        if (!updated)
        {
            throw new InvalidOperationException("The action result could not be applied because the command is not currently leased to this agent.");
        }
    }

    public Task<IReadOnlyList<AgentListItemDto>> GetAgentsAsync(CancellationToken cancellationToken)
        => _centralStore.GetAgentsAsync(cancellationToken);

    public Task<AgentDetailDto?> GetAgentDetailAsync(Guid agentId, CancellationToken cancellationToken)
        => _centralStore.GetAgentDetailAsync(agentId, cancellationToken);

    public Task<IReadOnlyList<CapabilitySnapshotHistoryItemDto>> GetCapabilityHistoryAsync(
        Guid agentId,
        string capabilityKey,
        int take,
        CancellationToken cancellationToken)
        => _centralStore.GetCapabilityHistoryAsync(agentId, capabilityKey, NormalizeTake(take, 5, 100), cancellationToken);

    public Task<IReadOnlyList<CapabilityChangeEventDto>> GetChangeFeedAsync(
        Guid? agentId,
        int take,
        CancellationToken cancellationToken)
        => _centralStore.GetChangeFeedAsync(agentId, NormalizeTake(take, 10, 100), cancellationToken);

    public Task<bool> ApproveAgentAsync(Guid agentId, CancellationToken cancellationToken)
        => _centralStore.ApproveAgentAsync(agentId, cancellationToken);

    public async Task<ActionCommandSummaryDto> CreateActionAsync(
        ActionCommandCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        var detail = await _centralStore.GetAgentDetailAsync(request.AgentId, cancellationToken);
        if (detail is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        if (!string.Equals(detail.Status, AgentStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Actions can only be queued for approved agents. Current status: {detail.Status}.");
        }

        if (!detail.Capabilities.Any(capability => string.Equals(
                capability.Descriptor.CapabilityKey,
                request.CapabilityKey,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Agent does not publish capability '{request.CapabilityKey}'.");
        }

        var capability = detail.Capabilities.First(capability => string.Equals(
            capability.Descriptor.CapabilityKey,
            request.CapabilityKey,
            StringComparison.OrdinalIgnoreCase));

        var action = capability.Descriptor.Actions.FirstOrDefault(definedAction => string.Equals(
            definedAction.ActionKey,
            request.ActionKey,
            StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            throw new InvalidOperationException(
                $"Capability '{request.CapabilityKey}' does not expose an action named '{request.ActionKey}'.");
        }

        if (action.RequiresTarget && string.IsNullOrWhiteSpace(request.TargetKey))
        {
            throw new InvalidOperationException(
                $"Action '{request.ActionKey}' on capability '{request.CapabilityKey}' requires a target.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            throw new InvalidOperationException("RequestedBy is required.");
        }

        if (await _centralStore.HasQueuedActionAsync(
                request.AgentId,
                request.CapabilityKey,
                request.ActionKey,
                request.TargetKey,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"There is already a queued or running '{request.ActionKey}' action for '{request.TargetKey ?? request.CapabilityKey}'.");
        }

        return await _centralStore.CreateActionAsync(request, cancellationToken);
    }

    public async Task<ActionCommandSummaryDto> CancelPendingActionAsync(
        Guid actionId,
        ActionCommandCancelRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            throw new InvalidOperationException("RequestedBy is required.");
        }

        var cancelled = await _centralStore.CancelPendingActionAsync(
            actionId,
            request.RequestedBy,
            request.Reason,
            cancellationToken);

        return cancelled
               ?? throw new InvalidOperationException("Only pending actions can be cancelled.");
    }

    private async Task<StoredAgent> GetAuthorizedAgentAsync(
        string installationId,
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedAccessException("Missing agent token.");
        }

        var agent = await _centralStore.GetAgentByInstallationIdAsync(installationId, cancellationToken);
        if (agent is null || !string.Equals(agent.AccessToken, token, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Invalid agent credentials.");
        }

        return agent;
    }

    private static void EnsureApproved(StoredAgent agent)
    {
        if (!string.Equals(agent.Status, AgentStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Agent is not approved. Current status: {agent.Status}.");
        }
    }

    private static int NormalizeTake(int requestedTake, int defaultTake, int maxTake)
        => requestedTake <= 0 ? defaultTake : Math.Min(requestedTake, maxTake);
}
