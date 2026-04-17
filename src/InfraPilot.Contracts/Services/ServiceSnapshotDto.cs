namespace InfraPilot.Contracts.Services;

public sealed class ServiceSnapshotDto
{
    public IReadOnlyList<ServiceStatusDto> Services { get; init; } = [];

    public ServiceSnapshotDto()
    {
    }

    public ServiceSnapshotDto(IReadOnlyList<ServiceStatusDto> services)
    {
        Services = services ?? [];
    }
}
