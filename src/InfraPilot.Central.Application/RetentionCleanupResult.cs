namespace InfraPilot.Central.Application;

public sealed record RetentionCleanupResult(
    int SnapshotsDeleted,
    int ChangeEventsDeleted,
    int ActionsDeleted);
