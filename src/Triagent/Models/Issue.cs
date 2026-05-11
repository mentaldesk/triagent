namespace Triagent.Models;

/// <summary>
/// A row in the issues table — the stored representation of a GitHub issue and its
/// most recent score (if any).
/// </summary>
public sealed record IssueRow
{
    public required string Repo { get; init; }
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string Labels { get; init; } = string.Empty;
    public required string State { get; init; }              // "open" | "closed"
    public bool HasLinkedPr { get; init; }
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
}

/// <summary>
/// Lightweight summary of a GitHub issue from the listing endpoint — no comments yet.
/// </summary>
public sealed record GitHubIssueSummary(
    int Number,
    string Title,
    string Url,
    string Labels,
    DateTimeOffset UpdatedAt);

/// <summary>
/// An issue plus its comments, ready to be passed to a scorer.
/// </summary>
public sealed record IssueForScoring(
    int Number,
    string Title,
    string Labels,
    string Body,
    IReadOnlyList<string> Comments);
