namespace InfraPilot.Capabilities.Services.Windows;

public sealed class ServicesCapabilityOptions
{
    public const string SectionName = "Capabilities:Services";

    public bool Enabled { get; set; } = true;

    public List<string> IncludeNames { get; set; } = [];

    public List<string> ExcludeNames { get; set; } = [];
}
