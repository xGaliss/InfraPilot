namespace InfraPilot.Agent.Core;

using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class FileAgentIdentityStore : IAgentIdentityStore
{
    private readonly AgentOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileAgentIdentityStore(IOptions<AgentOptions> options)
    {
        _options = options.Value;
    }

    public async Task<AgentIdentity> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var path = GetIdentityPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            var existing = await JsonSerializer.DeserializeAsync<AgentIdentity>(stream, JsonOptions, cancellationToken);
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.InstallationId))
            {
                return existing;
            }
        }

        var identity = new AgentIdentity();
        await SaveAsync(identity, cancellationToken);
        return identity;
    }

    public async Task SaveAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var path = GetIdentityPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, identity, JsonOptions, cancellationToken);
    }

    private string GetIdentityPath()
        => Path.Combine(Path.GetFullPath(_options.DataDirectory), "agent-identity.json");
}
