namespace InfraPilot.Contracts.ScheduledTasks;

public sealed record ScheduledTaskSnapshotDto(IReadOnlyList<ScheduledTaskInfoDto> Tasks);
