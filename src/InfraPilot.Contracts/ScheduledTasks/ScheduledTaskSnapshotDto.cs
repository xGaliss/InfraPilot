namespace InfraPilot.Contracts.ScheduledTasks;

public sealed class ScheduledTaskSnapshotDto
{
    public IReadOnlyList<ScheduledTaskInfoDto> Tasks { get; init; } = [];

    public ScheduledTaskSnapshotDto()
    {
    }

    public ScheduledTaskSnapshotDto(IReadOnlyList<ScheduledTaskInfoDto> tasks)
    {
        Tasks = tasks ?? [];
    }
}
