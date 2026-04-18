namespace InfraPilot.Agent.Core;

public sealed class AgentUnauthorizedException : Exception
{
    public AgentUnauthorizedException(string message)
        : base(message)
    {
    }
}
