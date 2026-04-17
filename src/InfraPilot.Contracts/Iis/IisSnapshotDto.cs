namespace InfraPilot.Contracts.Iis;

public sealed class IisSnapshotDto
{
    public IReadOnlyList<IisAppPoolDto> AppPools { get; init; } = [];

    public IReadOnlyList<IisSiteDto> Sites { get; init; } = [];

    public IisSnapshotDto()
    {
    }

    public IisSnapshotDto(IReadOnlyList<IisAppPoolDto> appPools, IReadOnlyList<IisSiteDto> sites)
    {
        AppPools = appPools ?? [];
        Sites = sites ?? [];
    }
}
