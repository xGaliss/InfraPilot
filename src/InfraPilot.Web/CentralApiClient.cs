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
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
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

    public async Task ApproveAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/agents/{agentId:D}/approve", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ActionCommandSummaryDto> CreateActionAsync(ActionCommandCreateRequestDto request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/actions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Central API rejected the action request with {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ActionCommandSummaryDto>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The central API returned an empty action payload.");
    }

    public async Task<ActionCommandSummaryDto> CancelActionAsync(
        Guid actionId,
        ActionCommandCancelRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/actions/{actionId:D}/cancel", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Central API rejected the cancel request with {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ActionCommandSummaryDto>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The central API returned an empty cancel payload.");
    }
}
