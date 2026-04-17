namespace InfraPilot.Agent.Core;

public sealed class AgentIdentity
{
    public string InstallationId { get; set; } = Guid.NewGuid().ToString("D");

    public Guid? AgentId { get; set; }

    public string? AccessToken { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
