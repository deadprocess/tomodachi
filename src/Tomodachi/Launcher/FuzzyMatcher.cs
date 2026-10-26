using System.Globalization;
using System.Text;

namespace Tomodachi.Launcher;

/// <summary>
/// Paket 3.2: bewertet, wie gut eine Eingabe auf einen App-Namen passt. Höher ist besser, null heißt „kein Treffer“.
/// Die Stufen liegen 100 Punkte auseinander, damit die Frecency innerhalb einer Stufe umsortiert,
/// aber einen klar besseren Treffer nur selten überholt.
/// </summary>
internal static class FuzzyMatcher
{
    public const int Exact = 1000;
    public const int Prefix = 900;
    public const int WordPrefix = 800;
    public const int Acronym = 750;
    public const int Substring = 600;
    public const int Subsequence = 300;

    public static int? Score(string query, string candidate)
    {
        var q = Normalize(query);
        var c = Normalize(candidate);
        if (q.Length == 0) return 0;
        if (c.Length == 0) return null;

        if (c == q) return Exact;
        if (c.StartsWith(q, StringComparison.Ordinal)) return Prefix - Math.Min(99, c.Length - q.Length);

        var words = Words(c);
        for (var i = 1; i < words.Count; i++)
        {
            if (words[i].StartsWith(q, StringComparison.Ordinal)) return WordPrefix - Math.Min(99, i * 10);
        }

        var initials = string.Concat(words.Select(w => w[0]));
        if (q.Length >= 2 && initials.StartsWith(q, StringComparison.Ordinal))
            return Acronym - Math.Min(49, initials.Length - q.Length);

        var index = c.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0) return Substring - Math.Min(99, index);

        return SubsequenceScore(q, c);
    }

    /// <summary>Alle Zeichen der Eingabe in Reihenfolge. Lücken kosten, aufeinanderfolgende Treffer bringen Bonus.</summary>
    private static int? SubsequenceScore(string q, string c)
    {
        if (q.Length < 2) return null;

        int qi = 0, gaps = 0, runs = 0, last = -2;
        for (var ci = 0; ci < c.Length && qi < q.Length; ci++)
        {
            if (c[ci] != q[qi]) continue;
            if (ci != last + 1)
            {
                runs++;
                if (last >= 0) gaps += ci - last - 1;
            }
            last = ci;
            qi++;
        }
        if (qi < q.Length) return null;

        // Zu zerfasert ist kein sinnvoller Treffer mehr („ae“ passt sonst auf fast alles)
        if (runs > Math.Max(3, q.Length / 2 + 1)) return null;
        return Subsequence - Math.Min(99, gaps * 3 + runs * 10);
    }

    private static List<string> Words(string normalized) =>
        normalized.Split([' ', '-', '_', '.', '(', ')', '&', '+', ','], StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Kleinbuchstaben, ohne Diakritika („Über“ → „uber“), Whitespace zusammengefasst.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }
            builder.Append(ch == 'ß' ? "ss" : ch.ToString());
            lastWasSpace = false;
        }
        return builder.ToString();
    }
}
