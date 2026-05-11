namespace Triagent.Models;

/// <summary>
/// One scorer's verdict for one issue. The five dimensions are each 1-3 (higher = more
/// agent-ready). <see cref="Total"/> is their sum (5-15).
/// </summary>
public sealed record IssueScore(
    int Number,
    int Specificity,
    int Containment,
    int Verifiability,
    int Clarity,
    int SelfSufficiency,
    string Rationale)
{
    public int Total => Specificity + Containment + Verifiability + Clarity + SelfSufficiency;
}

public sealed record AnalyzeSummary(
    int OpenIssues,
    int Scored,
    int SkippedUnchanged,
    int SkippedPrLinked,
    int NewlyClosed,
    int Failed);
