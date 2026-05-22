using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.RiskLevelQuestionDiff;

public static class TextSimilarity
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static double Score(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);

        if (na.Length == 0 && nb.Length == 0) return 1.0;
        if (na.Length == 0 || nb.Length == 0) return 0.0;

        int dist = LevenshteinDistance(na, nb);
        return 1.0 - dist / (double)Math.Max(na.Length, nb.Length);
    }

    private static string Normalize(string s)
        => Whitespace.Replace(s.Trim(), " ").ToLowerInvariant();

    private static int LevenshteinDistance(string a, string b)
    {
        int m = a.Length;
        int n = b.Length;
        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int j = 0; j <= n; j++) prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                if (a[i - 1] == b[j - 1])
                    curr[j] = prev[j - 1];
                else
                    curr[j] = 1 + Math.Min(prev[j - 1], Math.Min(prev[j], curr[j - 1]));
            }
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
