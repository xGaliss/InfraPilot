namespace InfraPilot.Web;

public sealed class CentralApiOptions
{
    public const string SectionName = "CentralApi";

    public string BaseUrl { get; set; } = "http://localhost:5180";

    public string OperatorApiKey { get; set; } = "infra-dev-operator";

    public bool AllowInsecureTransport { get; set; } = true;
}
