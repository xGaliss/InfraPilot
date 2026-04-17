namespace InfraPilot.Web;

public sealed class CentralApiOptions
{
    public const string SectionName = "CentralApi";

    public string BaseUrl { get; set; } = "http://localhost:5180";
}
