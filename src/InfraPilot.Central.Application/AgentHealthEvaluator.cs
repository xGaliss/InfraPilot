namespace InfraPilot.Central.Application;

using InfraPilot.Contracts.Common;

public static class AgentHealthEvaluator
{
    public static string Compute(string agentStatus, DateTimeOffset? lastSeenUtc, CentralOptions options, DateTimeOffset nowUtc)
    {
        if (string.Equals(agentStatus, AgentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return AgentHealthStatuses.NeedsApproval;
        }

        if (string.Equals(agentStatus, AgentStatuses.Revoked, StringComparison.OrdinalIgnoreCase))
        {
            return AgentHealthStatuses.Revoked;
        }

        if (lastSeenUtc is null)
        {
            return AgentHealthStatuses.Unknown;
        }

        var age = nowUtc - lastSeenUtc.Value;
        if (age <= TimeSpan.FromSeconds(Math.Max(15, options.HealthyThresholdSeconds)))
        {
            return AgentHealthStatuses.Healthy;
        }

        if (age <= TimeSpan.FromSeconds(Math.Max(options.HealthyThresholdSeconds + 1, options.DelayedThresholdSeconds)))
        {
            return AgentHealthStatuses.Delayed;
        }

        return AgentHealthStatuses.Offline;
    }
}
