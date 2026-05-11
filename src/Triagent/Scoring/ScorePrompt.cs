using System.Text;
using Triagent.Models;

namespace Triagent.Scoring;

/// <summary>
/// Shared prompt template for all CLI-based scorers. Returns a single rubric prompt
/// containing every issue in the batch.
/// </summary>
public static class ScorePrompt
{
    private const int MaxBodyChars = 2000;
    private const int MaxCommentChars = 500;
    private const int MaxComments = 10;

    public const string Rubric = """
        Score these GitHub issues for AI-agent doability.

        Each issue is scored 1-3 on five dimensions, higher = more agent-ready:

        1. SPECIFICITY: desired outcome clearly defined?
           1=vague  2=implied  3=clearly stated

        2. CONTAINMENT: change bounded and self-contained?
           1=cross-cutting  2=moderate scope  3=tight/self-contained

        3. VERIFIABILITY: can success be objectively confirmed?
           1=no clear measure  2=inferable  3=explicit criteria or tests exist

        4. CLARITY: pure implementation, no product decisions needed?
           1=agent must make design decisions  2=some interpretation  3=pure implementation

        5. SELF_SUFFICIENCY: understandable without extra context?
           1=needs history/reporter/expertise  2=mostly stands alone  3=fully self-contained

        Respond with ONE raw JSON array (no markdown fences, no prose), one object per issue:
        [
          {
            "number": <int>,
            "specificity": <1-3>,
            "containment": <1-3>,
            "verifiability": <1-3>,
            "clarity": <1-3>,
            "self_sufficiency": <1-3>,
            "rationale": "<one sentence>"
          }
        ]

        ISSUES:

        """;

    public static string Build(IReadOnlyList<IssueForScoring> batch)
    {
        var sb = new StringBuilder(Rubric);
        foreach (var issue in batch)
        {
            sb.Append("--- ISSUE #").Append(issue.Number).AppendLine(" ---");
            sb.Append("TITLE: ").AppendLine(issue.Title);
            sb.Append("LABELS: ").AppendLine(issue.Labels);
            sb.AppendLine("BODY:");
            sb.AppendLine(Truncate(issue.Body, MaxBodyChars));

            var commentsToInclude = issue.Comments.Take(MaxComments).ToList();
            if (commentsToInclude.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("COMMENTS:");
                for (var i = 0; i < commentsToInclude.Count; i++)
                {
                    sb.Append('[').Append(i + 1).Append("] ")
                      .AppendLine(Truncate(commentsToInclude[i], MaxCommentChars));
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= max ? s : s[..max] + "…");
}
