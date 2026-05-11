using System.Text;

namespace Triagent.Scoring;

/// <summary>
/// Hand-curated examples from the initial sentry-dotnet analysis, used as in-prompt
/// calibration anchors so successive runs (and successive models) score consistently.
///
/// See <see href="https://github.com/mentaldesk/triagent/issues/3"/> for the rationale.
/// </summary>
internal static class CalibrationExamples
{
    private sealed record Example(
        int Number,
        string Title,
        int Specificity,
        int Containment,
        int Verifiability,
        int Clarity,
        int SelfSufficiency,
        string Rationale)
    {
        public int Total => Specificity + Containment + Verifiability + Clarity + SelfSufficiency;
    }

    private static readonly Example[] Examples =
    [
        // High — both 15/15
        new(5091,
            "`http.server` spans are incorrectly generated for non-2xx status codes when using OTel integration",
            3, 3, 3, 3, 3,
            "Textbook agent-ready bug: exact expected vs. actual behavior, a public minimal repro repo, trace JSON attached, and a comment confirms a clear narrow fix."),
        new(4733,
            "Add device attributes to Logs in the MAUI SDK",
            3, 3, 3, 3, 3,
            "Names the exact attributes needed, scopes the change to the MAUI SDK logs layer, and a comment provides a working workaround that maps directly to the SDK-level implementation."),

        // Medium upper — score 9
        new(5157,
            "ci: Auto-Verify API changes in Pull Requests",
            2, 2, 2, 1, 2,
            "Goal is clear and a model workflow exists to copy, but the issue presents an alternative architecture (PublicApiAnalyzers) and leaves the implementation choice open — requires a product decision before coding."),
        new(5149,
            "test: CoreCLR iOS integration tests",
            2, 2, 2, 2, 1,
            "Concrete TODO marker and Android precedent (PR #5127) make it traceable, but completion requires deep iOS/MAUI/CoreCLR ecosystem knowledge and .NET 11 preview tooling that isn't self-described."),

        // Medium lower — score 7
        new(3292,
            "Add ServiceCollection extensions for native Android and iOS projects",
            2, 1, 1, 1, 2,
            "Desired API shape is loosely sketched but naming, target frameworks, and lifecycle integration are still being negotiated in comments — a design conversation, not an implementation task."),
        new(3154,
            "Sentry implementation of `IExceptionHandler`",
            1, 2, 1, 1, 2,
            "Author is explicitly unsure whether the feature is wanted or how it should behave — exploratory product proposal rather than implementable task."),

        // Low — both 5/15
        new(757,
            "Explore Windows specific APIs for extra context",
            1, 1, 1, 1, 1,
            "Purely exploratory, no defined APIs, no acceptance criteria, and a concluding comment defers the work indefinitely pending future requirements."),
        new(285,
            "device context not included in new sentry .net sdk",
            1, 1, 1, 1, 1,
            "After years of comments the scope is still unresolved — depends on framework (WPF, UWP, console, etc.), requires platform-specific expertise, and involves product decisions about cross-platform feasibility.")
    ];

    /// <summary>
    /// Renders the calibration block to include in the scoring prompt. Groups
    /// examples by total score so the model sees the contrast between bands.
    /// </summary>
    public static string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CALIBRATION EXAMPLES — anchor your scoring to these reference rulings from a prior analysis. Use the same calibration in this run.");
        sb.AppendLine();

        foreach (var group in Examples.GroupBy(e => e.Total).OrderByDescending(g => g.Key))
        {
            sb.Append("Total ").Append(group.Key).Append("/15");
            sb.AppendLine(group.Key switch
            {
                >= 13 => " — agent-ready:",
                >= 9 => " — needs clarification first:",
                _ => " — human triage required:",
            });
            foreach (var ex in group)
            {
                sb.Append("  #").Append(ex.Number).Append(' ').Append('"').Append(ex.Title).AppendLine("\"");
                sb.AppendLine($"    specificity={ex.Specificity}, containment={ex.Containment}, verifiability={ex.Verifiability}, clarity={ex.Clarity}, self_sufficiency={ex.SelfSufficiency}");
                sb.Append("    Why: ").AppendLine(ex.Rationale);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
