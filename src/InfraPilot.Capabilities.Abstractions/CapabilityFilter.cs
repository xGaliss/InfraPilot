namespace InfraPilot.Capabilities.Abstractions;

public static class CapabilityFilter
{
    public static bool Matches(string candidate, IReadOnlyCollection<string>? includePatterns, IReadOnlyCollection<string>? excludePatterns)
    {
        candidate ??= string.Empty;

        var includeMatch = includePatterns is null || includePatterns.Count == 0
            || includePatterns.Any(pattern => Contains(candidate, pattern));

        if (!includeMatch)
        {
            return false;
        }

        return excludePatterns is null
               || excludePatterns.Count == 0
               || !excludePatterns.Any(pattern => Contains(candidate, pattern));
    }

    private static bool Contains(string candidate, string pattern)
        => !string.IsNullOrWhiteSpace(pattern)
           && candidate.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase);
}
