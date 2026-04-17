namespace InfraPilot.Agent.Core;

using System.Text.Json;
using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using Microsoft.Extensions.Logging;

public sealed class AgentRuntimeCoordinator
{
    private readonly IAgentIdentityStore _identityStore;
    private readonly ICentralAgentApiClient _centralAgentApiClient;
    private readonly Dictionary<string, ICapabilityModule> _capabilityModules;
    private readonly ILogger<AgentRuntimeCoordinator> _logger;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AgentRuntimeCoordinator(
        IAgentIdentityStore identityStore,
        ICentralAgentApiClient centralAgentApiClient,
        IEnumerable<ICapabilityModule> capabilityModules,
        ILogger<AgentRuntimeCoordinator> logger)
    {
        _identityStore = identityStore;
        _centralAgentApiClient = centralAgentApiClient;
        _logger = logger;
        _capabilityModules = capabilityModules.ToDictionary(
            module => module.Describe().CapabilityKey,
            module => module,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        await _registrationLock.WaitAsync(cancellationToken);

        try
        {
            var identity = await _identityStore.GetOrCreateAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(identity.AccessToken))
            {
                return;
            }

            var response = await _centralAgentApiClient.EnrollAsync(identity, cancellationToken);
            identity.AgentId = response.AgentId;
            identity.AccessToken = response.AccessToken;
            await _identityStore.SaveAsync(identity, cancellationToken);

            _logger.LogInformation(
                "Agent enrolled successfully. AgentId={AgentId} InstallationId={InstallationId} Status={Status}",
                response.AgentId,
                identity.InstallationId,
                response.Status);
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    public async Task PublishCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken);
        var descriptors = _capabilityModules.Values
            .Select(module => module.Describe())
            .OrderBy(descriptor => descriptor.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _centralAgentApiClient.PublishCapabilitiesAsync(identity, descriptors, cancellationToken);
        _logger.LogDebug("Published {Count} capability descriptors.", descriptors.Count);
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken);
        await _centralAgentApiClient.SendHeartbeatAsync(identity, cancellationToken);
    }

    public async Task PublishSnapshotsAsync(CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken);
        var snapshots = new List<CapabilitySnapshotDto>();

        foreach (var capabilityModule in _capabilityModules.Values.OrderBy(module => module.Describe().CapabilityKey, StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = await capabilityModule.CollectSnapshotAsync(cancellationToken);
            var payloadJson = JsonSerializer.Serialize(snapshot.Payload, JsonOptions);
            snapshots.Add(new CapabilitySnapshotDto(
                snapshot.CapabilityKey,
                snapshot.SchemaVersion,
                SnapshotHashing.Compute(payloadJson),
                payloadJson));
        }

        await _centralAgentApiClient.PublishCapabilitiesAsync(
            identity,
            _capabilityModules.Values.Select(module => module.Describe()).ToList(),
            cancellationToken);

        await _centralAgentApiClient.PublishSnapshotsAsync(identity, snapshots, cancellationToken);
        _logger.LogInformation("Published {Count} capability snapshots.", snapshots.Count);
    }

    public async Task TryExecuteNextActionAsync(CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken);
        var command = await _centralAgentApiClient.PullNextActionAsync(identity, cancellationToken);
        if (command is null)
        {
            return;
        }

        if (!_capabilityModules.TryGetValue(command.CapabilityKey, out var module))
        {
            await _centralAgentApiClient.ReportActionResultAsync(
                identity,
                command.ActionId,
                new AgentActionResultReportDto(
                    identity.InstallationId,
                    "Failed",
                    $"Capability '{command.CapabilityKey}' is not registered in this agent.",
                    "Capability not found.",
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            return;
        }

        try
        {
            var result = await module.ExecuteActionAsync(command, cancellationToken);
            await _centralAgentApiClient.ReportActionResultAsync(
                identity,
                command.ActionId,
                new AgentActionResultReportDto(
                    identity.InstallationId,
                    result.Succeeded ? "Succeeded" : "Failed",
                    result.ResultMessage,
                    result.ErrorMessage,
                    result.OutputJson,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _centralAgentApiClient.ReportActionResultAsync(
                identity,
                command.ActionId,
                new AgentActionResultReportDto(
                    identity.InstallationId,
                    "Failed",
                    exception.Message,
                    exception.ToString(),
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task<AgentIdentity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        await EnsureRegisteredAsync(cancellationToken);
        return await _identityStore.GetOrCreateAsync(cancellationToken);
    }
}
