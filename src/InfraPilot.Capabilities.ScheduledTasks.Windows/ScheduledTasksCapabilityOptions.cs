namespace InfraPilot.Capabilities.ScheduledTasks.Windows;

public sealed class ScheduledTasksCapabilityOptions
{
    public const string SectionName = "Capabilities:ScheduledTasks";

    public bool Enabled { get; set; } = true;

    public List<string> IncludePaths { get; set; } = [];

    public List<string> ExcludePaths { get; set; } = [];
}
