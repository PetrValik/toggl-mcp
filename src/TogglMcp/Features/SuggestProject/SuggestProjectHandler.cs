using TogglMcp.Contracts;
using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.SuggestProject;

/// <summary>
/// The only logic layer of the suggest_project slice. Maps a work item (source calendar/list
/// name + title) to a Toggl project in two tiers: learned per-user keyword rules first, then a
/// fuzzy name-similarity fallback. Never persists anything — a chosen mapping is saved elsewhere
/// via add_mapping_rule.
/// </summary>
internal sealed class SuggestProjectHandler(
    ITogglClient toggl,               // source of the user's live project list
    IMappingRuleStore rules,          // learned keyword -> project rules, scoped per user
    ITogglTokenProvider tokens)       // supplies UserKey to scope the rule lookup to this user
{
    /// <summary>
    /// Suggests a project for the given source name + title. Returns a confident, silent match
    /// when a learned rule fires; otherwise a low-confidence guess the caller should confirm.
    /// </summary>
    /// <param name="calendarName">Source calendar/board/list name (may be empty).</param>
    /// <param name="title">Title/summary of the work item.</param>
    public async Task<SuggestProjectResult> HandleAsync(string calendarName, string title, CancellationToken cancellationToken)
    {
        // Live project set, indexed by id so a rule's ProjectId can be resolved (and stale rules skipped).
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var byId = projects.ToDictionary(project => project.Id);
        // Lower-cased combined source text; rule keywords are tested for containment against this.
        var haystack = $"{calendarName} {title}".ToLowerInvariant();

        // 1) Learned rules win — first keyword contained in the source text, whose project still exists.
        var userRules = await rules.GetRulesAsync(tokens.UserKey, cancellationToken);
        foreach (var rule in userRules)
        {
            // First rule whose keyword is present AND whose project still exists wins; 0.95 signals "act silently".
            if (haystack.Contains(rule.Keyword.ToLowerInvariant(), StringComparison.Ordinal) && byId.TryGetValue(rule.ProjectId, out var project))
                return new SuggestProjectResult(true, project.Id, project.Name, 0.95,
                    $"Matched learned rule '{rule.Keyword}' -> {project.Name}.", [project.ToDto()]);
        }

        // 2) Fuzzy fallback against project names — score every project, highest similarity first.
        var scored = projects
            .Select(project => (project, score: Similarity($"{calendarName} {title}", project.Name)))
            .OrderByDescending(x => x.score)
            .ToList();

        // Offer the top 3 regardless of confidence, so the user can pick even when nothing is strong.
        var candidates = scored.Take(3).Select(x => x.project.ToDto()).ToList();
        var best = scored.FirstOrDefault();
        // Below the 0.15 floor (or no projects at all) we refuse to guess and defer to the user.
        if (best.project is null || best.score < 0.15)
            return new SuggestProjectResult(false, null, null, best.project is null ? 0 : Math.Round(best.score, 2),
                "No confident match — ask the user which project, then call add_mapping_rule.", candidates);

        // A named guess above the floor — still Matched=false so the caller confirms before logging.
        return new SuggestProjectResult(false, best.project.Id, best.project.Name, Math.Round(best.score, 2),
            $"Best guess by name similarity ({best.score:P0}); confirm with the user before logging.", candidates);
    }

    /// <summary>Token-set Jaccard similarity with a substring boost. 0..1.</summary>
    private static double Similarity(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        // No shared vocabulary is possible if either side tokenizes to nothing.
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var intersection = ta.Intersect(tb).Count();
        var union = ta.Union(tb).Count();
        var jaccard = (double)intersection / union;
        // Reward a full-name substring hit (e.g. "Acme" inside the source text); guard tiny names to avoid noise.
        var boost = b.Length >= 3 && a.Contains(b, StringComparison.OrdinalIgnoreCase) ? 0.3 : 0;
        return Math.Min(1.0, jaccard + boost);   // clamp so boost can't push past 1.0
    }

    // Lower-case, split on whitespace then on punctuation, and keep tokens of length >= 2 as a set.
    private static HashSet<string> Tokens(string s) =>
        s.ToLowerInvariant()
         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
         .SelectMany(word => word.Split(['-', '_', '/', '.', ',', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
         .Where(word => word.Length >= 2)
         .ToHashSet();
}
