namespace InfraPilot.Contracts.ScheduledTasks;

public sealed record ScheduledTaskInfoDto(
    string TaskName,
    string TaskPath,
    string Status,
    string? Author,
    string? RunAsUser,
    string? LastRunTimeRaw,
    string? NextRunTimeRaw,
    string? LastResultRaw,
    string? TaskToRun);
