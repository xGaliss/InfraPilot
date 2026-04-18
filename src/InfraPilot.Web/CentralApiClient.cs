namespace InfraPilot.Web;

using System.Net.Http.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;
using Microsoft.Extensions.Options;

public sealed class CentralApiClient
{
    private readonly HttpClient _httpClient;

    public CentralApiClient(HttpClient httpClient, IOptions<CentralApiOptions> options)
    {
        _httpClient = httpClient;
        var configuredOptions = options.Value;
        ValidateTransport(configuredOptions);
        _httpClient.BaseAddress = new Uri(configuredOptions.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("x-operator-key");
        _httpClient.DefaultRequestHeaders.Add("x-operator-key", configuredOptions.OperatorApiKey);
    }

    public async Task<IReadOnlyList<AgentListItemDto>> GetAgentsAsync(CancellationToken cancellationToken)
        => await _httpClient.GetFromJsonAsync<IReadOnlyList<AgentListItemDto>>("api/agents", cancellationToken)
           ?? [];

    public async Task<AgentDetailDto?> GetAgentDetailAsync(Guid agentId, CancellationToken cancellationToken)
        => await _httpClient.GetFromJsonAsync<AgentDetailDto>($"api/agents/{agentId:D}", cancellationToken);

    public async Task<IReadOnlyList<CapabilityChangeEventDto>> GetChangeFeedAsync(Guid? agentId, int take, CancellationToken cancellationToken)
    {
        var uri = agentId is null
            ? $"api/changes?take={take}"
            : $"api/agents/{agentId:D}/changes?take={take}";

        return await _httpClient.GetFromJsonAsync<IReadOnlyList<CapabilityChangeEventDto>>(uri, cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<CapabilitySnapshotHistoryItemDto>> GetCapabilityHistoryAsync(
        Guid agentId,
        string capabilityKey,
        int take,
        CancellationToken cancellationToken)
        => await _httpClient.GetFromJsonAsync<IReadOnlyList<CapabilitySnapshotHistoryItemDto>>(
               $"api/agents/{agentId:D}/capabilities/{capabilityKey}/history?take={take}",
               cancellationToken)
           ?? [];

    public async Task<AgentControlResultDto> ApproveAgentAsync(
        Guid agentId,
        AgentControlRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/agents/{agentId:D}/approve", request, cancellationToken);
        return await ReadRequiredAsync<AgentControlResultDto>(response, "approve", cancellationToken);
    }

    public async Task<AgentControlResultDto> RevokeAgentAsync(
        Guid agentId,
        AgentControlRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/agents/{agentId:D}/revoke", request, cancellationToken);
        return await ReadRequiredAsync<AgentControlResultDto>(response, "revoke", cancellationToken);
    }

    public async Task<AgentControlResultDto> ResetAgentTokenAsync(
        Guid agentId,
        AgentControlRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/agents/{agentId:D}/reset-token", request, cancellationToken);
        return await ReadRequiredAsync<AgentControlResultDto>(response, "reset token", cancellationToken);
    }

    public async Task<ActionCommandSummaryDto> CreateActionAsync(ActionCommandCreateRequestDto request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/actions", request, cancellationToken);
        return await ReadRequiredAsync<ActionCommandSummaryDto>(response, "queue action", cancellationToken);
    }

    public async Task<ActionCommandSummaryDto> CancelActionAsync(
        Guid actionId,
        ActionCommandCancelRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/actions/{actionId:D}/cancel", request, cancellationToken);
        return await ReadRequiredAsync<ActionCommandSummaryDto>(response, "cancel action", cancellationToken);
    }

    private static void ValidateTransport(CentralApiOptions options)
    {
        var uri = new Uri(options.BaseUrl, UriKind.Absolute);
        if (options.AllowInsecureTransport || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"CentralApi:BaseUrl '{options.BaseUrl}' uses insecure HTTP while AllowInsecureTransport is disabled.");
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Central API rejected the {operationName} request with {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"The central API returned an empty payload for {operationName}.");
    }
}
