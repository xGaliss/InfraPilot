namespace InfraPilot.Contracts.Iis;

public sealed record IisSnapshotDto(
    IReadOnlyList<IisAppPoolDto> AppPools,
    IReadOnlyList<IisSiteDto> Sites);
