namespace InfraPilot.Web;

using System.Net.Http.Json;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
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
        => await _httpClient.GetFromJsonAsync<AgentDetailDto>($"api/agents/{agentId:guid}", cancellationToken);

    public async Task ApproveAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/agents/{agentId:guid}/approve", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ActionCommandSummaryDto> CreateActionAsync(ActionCommandCreateRequestDto request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/actions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActionCommandSummaryDto>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The central API returned an empty action payload.");
    }
}
