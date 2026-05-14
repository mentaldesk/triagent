namespace Triagent.Models;

public enum ReviewDecision
{
    None = 0,
    Try = 1,
    Skip = 2,
}

public static class ReviewDecisionExtensions
{
    public static string? ToDbValue(this ReviewDecision decision) => decision switch
    {
        ReviewDecision.None => null,
        ReviewDecision.Try => "try",
        ReviewDecision.Skip => "skip",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown decision value."),
    };

    public static ReviewDecision FromDbValue(string? value) => value switch
    {
        null => ReviewDecision.None,
        "" => ReviewDecision.None,
        "try" => ReviewDecision.Try,
        "skip" => ReviewDecision.Skip,
        _ => ReviewDecision.None,
    };

    public static string ToDisplay(this ReviewDecision decision) => decision switch
    {
        ReviewDecision.None => "Unreviewed",
        ReviewDecision.Try => "Try",
        ReviewDecision.Skip => "Skip",
        _ => "Unreviewed",
    };
}

public sealed record IssueRow
{
    public required string Repo { get; init; }
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string Labels { get; init; } = string.Empty;
    public required string State { get; init; }
    public bool HasLinkedPr { get; init; }
    public bool HasAssignees { get; init; }
    public string Assignees { get; init; } = string.Empty;
    public required DateTimeOffset GitHubUpdatedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public DateTimeOffset? LastScoredAt { get; init; }
    public string? Scorer { get; init; }
    public int? Specificity { get; init; }
    public int? Containment { get; init; }
    public int? Verifiability { get; init; }
    public int? Clarity { get; init; }
    public int? SelfSufficiency { get; init; }
    public int? Total { get; init; }
    public string? Rationale { get; init; }
    public ReviewDecision ReviewDecision { get; init; }
    public string? ReviewReason { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
}

public sealed record GitHubIssueSummary(
    int Number,
    string Title,
    string Url,
    string Labels,
    bool HasAssignees,
    string Assignees,
    DateTimeOffset UpdatedAt)
{

}

public sealed record IssueForScoring(
    int Number,
    string Title,
    string Labels,
    string Body,
    IReadOnlyList<string> Comments);
