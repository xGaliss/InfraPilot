namespace InfraPilot.Central.Application;

public sealed class CentralOptions
{
    public const string SectionName = "Central";

    public string EnrollmentKey { get; set; } = "infra-dev-enroll";

    public bool AutoApproveAgents { get; set; } = true;

    public int ActionLeaseSeconds { get; set; } = 90;

    public int HealthyThresholdSeconds { get; set; } = 90;

    public int DelayedThresholdSeconds { get; set; } = 300;

    public string DatabasePath { get; set; } = "data\\central\\infrapilot.db";
}
