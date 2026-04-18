namespace InfraPilot.Web;

public sealed class OperatorAuthOptions
{
    public const string SectionName = "OperatorAuth";

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = "infra-dev-password";
}
