namespace Triagent.Scoring;

/// <summary>
/// Scorer that shells out to the ChatGPT Codex CLI. Codex's flag for a one-shot
/// non-interactive run is `exec` (with the prompt as a positional argument).
/// </summary>
public sealed class CodexCliScorer : CliScorerBase
{
    private readonly string? _model;

    public CodexCliScorer(string? model = null)
    {
        _model = model;
    }

    public override string Name => "codex";

    protected override string Executable => "codex";

    protected override IReadOnlyList<string> BuildArguments(string prompt)
    {
        var args = new List<string> { "exec" };
        if (!string.IsNullOrEmpty(_model))
        {
            args.Add("--model");
            args.Add(_model);
        }
        args.Add(prompt);
        return args;
    }
}
