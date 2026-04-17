namespace InfraPilot.Agent.Core;

using System.Net.Http.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class CentralAgentApiClient : ICentralAgentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AgentOptions _options;
    private readonly ILogger<CentralAgentApiClient> _logger;

    public CentralAgentApiClient(
        HttpClient httpClient,
        IOptions<AgentOptions> options,
        ILogger<CentralAgentApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.CentralBaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.HttpTimeoutSeconds));
    }

    public async Task<AgentEnrollResponseDto> EnrollAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/enroll");
        request.Headers.Add("x-enrollment-key", _options.EnrollmentKey);
        request.Content = JsonContent.Create(new AgentEnrollRequestDto(
            identity.InstallationId,
            _options.DisplayName,
            Environment.MachineName,
            _options.AgentVersion));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AgentEnrollResponseDto>(cancellationToken: cancellationToken);
        return payload ?? throw new InvalidOperationException("Central enrollment returned an empty response.");
    }

    public async Task SendHeartbeatAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/heartbeat");
        AddAgentHeaders(request, identity);
        request.Content = JsonContent.Create(new AgentHeartbeatRequestDto(
            identity.InstallationId,
            _options.AgentVersion,
            DateTimeOffset.UtcNow));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishCapabilitiesAsync(
        AgentIdentity identity,
        IReadOnlyList<CapabilityDescriptorDto> capabilities,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/capabilities");
        AddAgentHeaders(request, identity);
        request.Content = JsonContent.Create(new AgentCapabilityPublishRequestDto(identity.InstallationId, capabilities));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishSnapshotsAsync(
        AgentIdentity identity,
        IReadOnlyList<CapabilitySnapshotDto> snapshots,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/snapshots");
        AddAgentHeaders(request, identity);
        request.Content = JsonContent.Create(new AgentSnapshotPublishRequestDto(
            identity.InstallationId,
            DateTimeOffset.UtcNow,
            snapshots));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AgentActionCommandDto?> PullNextActionAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/actions/pull");
        AddAgentHeaders(request, identity);
        request.Content = JsonContent.Create(new AgentPullRequestDto(identity.InstallationId));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentActionCommandDto>(cancellationToken: cancellationToken);
    }

    public async Task ReportActionResultAsync(
        AgentIdentity identity,
        Guid actionId,
        AgentActionResultReportDto result,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/actions/{actionId:guid}/result");
        AddAgentHeaders(request, identity);
        request.Content = JsonContent.Create(result);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void AddAgentHeaders(HttpRequestMessage request, AgentIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.AccessToken))
        {
            throw new InvalidOperationException("Agent access token is not available.");
        }

        request.Headers.Add("x-agent-token", identity.AccessToken);
    }
}
