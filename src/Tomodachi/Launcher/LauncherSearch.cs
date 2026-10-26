namespace Tomodachi.Launcher;

internal static class LauncherSearch
{
    /// <summary>Der Alias (Exe- oder Paketname) zählt etwas weniger als der angezeigte Name.</summary>
    private const double AliasWeight = 0.85;

    /// <summary>
    /// Leere Eingabe: zuletzt und häufig genutzte Apps zuerst, danach alphabetisch.
    /// Sonst: Match-Score + Frecency, bei Gleichstand der kürzere Name.
    /// </summary>
    public static IReadOnlyList<AppEntry> Search(string query, IReadOnlyList<AppEntry> apps, LaunchHistory history,
        DateTime nowUtc, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return apps
                .OrderByDescending(a => history.Frecency(a.Id, nowUtc))
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .ToList();
        }

        var scored = new List<(AppEntry App, double Score)>();
        foreach (var app in apps)
        {
            var nameScore = FuzzyMatcher.Score(query, app.Name);
            var aliasScore = app.Alias is null ? null : FuzzyMatcher.Score(query, app.Alias) * AliasWeight;
            var best = Math.Max(nameScore ?? double.MinValue, aliasScore ?? double.MinValue);
            if (best == double.MinValue) continue;
            scored.Add((app, best + history.Frecency(app.Id, nowUtc)));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.App.Name.Length)
            .ThenBy(s => s.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(s => s.App)
            .ToList();
    }
}
