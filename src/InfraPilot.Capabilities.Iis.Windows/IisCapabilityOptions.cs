namespace InfraPilot.Capabilities.Iis.Windows;

public sealed class IisCapabilityOptions
{
    public const string SectionName = "Capabilities:Iis";

    public bool Enabled { get; set; } = true;

    public List<string> IncludeSites { get; set; } = [];

    public List<string> ExcludeSites { get; set; } = [];

    public List<string> IncludeAppPools { get; set; } = [];

    public List<string> ExcludeAppPools { get; set; } = [];
}
