using Triagent.Models;

namespace Triagent.Scoring;

public interface IScorer
{
    string Name { get; }

    /// <summary>
    /// Score a batch of issues. Implementations should return one <see cref="IssueScore"/>
    /// per input; if the model fails on an individual issue the implementation may
    /// return fewer items, and the caller will treat the missing ones as failures.
    /// </summary>
    Task<IReadOnlyList<IssueScore>> ScoreBatchAsync(
        IReadOnlyList<IssueForScoring> batch,
        CancellationToken ct);
}
