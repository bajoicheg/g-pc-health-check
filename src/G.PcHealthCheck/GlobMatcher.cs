namespace G.PcHealthCheck;

internal static class GlobMatcher
{
    public static bool IsMatch(string pattern, string candidate)
    {
        if (pattern is null || candidate is null) return false;

        var p = 0;
        var c = 0;
        var star = -1;
        var retryCandidate = 0;

        while (c < candidate.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || EqualsIgnoreCase(pattern[p], candidate[c])))
            {
                p++;
                c++;
                continue;
            }

            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                retryCandidate = c;
                continue;
            }

            if (star >= 0)
            {
                p = star + 1;
                c = ++retryCandidate;
                continue;
            }

            return false;
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool EqualsIgnoreCase(char left, char right)
        => char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
