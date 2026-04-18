namespace InfraPilot.Central.Application;

public sealed class CentralOptions
{
    public const string SectionName = "Central";

    public string EnrollmentKey { get; set; } = "infra-dev-enroll";

    public string OperatorApiKey { get; set; } = "infra-dev-operator";

    public bool AutoApproveAgents { get; set; } = false;

    public int ActionLeaseSeconds { get; set; } = 90;

    public int HealthyThresholdSeconds { get; set; } = 90;

    public int DelayedThresholdSeconds { get; set; } = 300;

    public int SnapshotRetentionDays { get; set; } = 30;

    public int ChangeEventRetentionDays { get; set; } = 30;

    public int ActionRetentionDays { get; set; } = 60;

    public int CleanupIntervalMinutes { get; set; } = 60;

    public string DatabasePath { get; set; } = "data\\central\\infrapilot.db";
}
