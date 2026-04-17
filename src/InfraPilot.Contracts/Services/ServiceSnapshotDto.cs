namespace InfraPilot.Contracts.Services;

public sealed record ServiceSnapshotDto(IReadOnlyList<ServiceStatusDto> Services);
