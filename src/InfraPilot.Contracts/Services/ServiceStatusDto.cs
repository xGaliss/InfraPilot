namespace InfraPilot.Contracts.Services;

public sealed record ServiceStatusDto(
    string ServiceName,
    string DisplayName,
    string Status,
    string CanStop,
    string ServiceType);
