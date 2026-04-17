namespace InfraPilot.Central.Application;

public sealed class CentralOptions
{
    public const string SectionName = "Central";

    public string EnrollmentKey { get; set; } = "infra-dev-enroll";

    public bool AutoApproveAgents { get; set; } = true;

    public int ActionLeaseSeconds { get; set; } = 90;

    public string DatabasePath { get; set; } = "data\\central\\infrapilot.db";
}
