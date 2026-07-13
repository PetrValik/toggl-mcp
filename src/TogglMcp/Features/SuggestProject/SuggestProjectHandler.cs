using TogglMcp.Contracts;
using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.SuggestProject;

internal sealed class SuggestProjectHandler(
    ITogglClient toggl,
    IMappingRuleStore rules,
    ITogglTokenProvider tokens)
{
    public async Task<SuggestProjectResult> HandleAsync(string calendarName, string title, CancellationToken cancellationToken)
    {
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var byId = projects.ToDictionary(project => project.Id);
        var haystack = $"{calendarName} {title}".ToLowerInvariant();

        // 1) Learned rules win — first keyword contained in the source text, whose project still exists.
        var userRules = await rules.GetRulesAsync(tokens.UserKey, cancellationToken);
        foreach (var rule in userRules)
        {
            if (haystack.Contains(rule.Keyword.ToLowerInvariant(), StringComparison.Ordinal) && byId.TryGetValue(rule.ProjectId, out var project))
                return new SuggestProjectResult(true, project.Id, project.Name, 0.95,
                    $"Matched learned rule '{rule.Keyword}' -> {project.Name}.", [project.ToDto()]);
        }

        // 2) Fuzzy fallback against project names.
        var scored = projects
            .Select(project => (project, score: Similarity($"{calendarName} {title}", project.Name)))
            .OrderByDescending(x => x.score)
            .ToList();

        var candidates = scored.Take(3).Select(x => x.project.ToDto()).ToList();
        var best = scored.FirstOrDefault();
        if (best.project is null || best.score < 0.15)
            return new SuggestProjectResult(false, null, null, best.project is null ? 0 : best.score,
                "No confident match — ask the user which project, then call add_mapping_rule.", candidates);

        return new SuggestProjectResult(false, best.project.Id, best.project.Name, Math.Round(best.score, 2),
            $"Best guess by name similarity ({best.score:P0}); confirm with the user before logging.", candidates);
    }

    /// <summary>Token-set Jaccard similarity with a substring boost. 0..1.</summary>
    private static double Similarity(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var intersection = ta.Intersect(tb).Count();
        var union = ta.Union(tb).Count();
        var jaccard = (double)intersection / union;
        var boost = b.Length >= 3 && a.Contains(b, StringComparison.OrdinalIgnoreCase) ? 0.3 : 0;
        return Math.Min(1.0, jaccard + boost);
    }

    private static HashSet<string> Tokens(string s) =>
        s.ToLowerInvariant()
         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
         .SelectMany(word => word.Split(['-', '_', '/', '.', ',', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
         .Where(word => word.Length >= 2)
         .ToHashSet();
}
