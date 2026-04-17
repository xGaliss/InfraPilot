namespace InfraPilot.Contracts.FileTree;

public sealed record FileTreeSnapshotDto(IReadOnlyList<FileTreeRootDto> Roots);
