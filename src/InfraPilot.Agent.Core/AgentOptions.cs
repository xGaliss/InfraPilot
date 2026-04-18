namespace InfraPilot.Agent.Core;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string DisplayName { get; set; } = Environment.MachineName;

    public string AgentVersion { get; set; } = "0.1.0";

    public string CentralBaseUrl { get; set; } = "http://localhost:5180";

    public bool AllowInsecureTransport { get; set; } = true;

    public string EnrollmentKey { get; set; } = "infra-dev-enroll";

    public string DataDirectory { get; set; } = "data\\agent";

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public int SnapshotIntervalSeconds { get; set; } = 60;

    public int ActionPollIntervalSeconds { get; set; } = 5;

    public int HttpTimeoutSeconds { get; set; } = 15;
}
