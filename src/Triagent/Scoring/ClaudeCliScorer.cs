namespace Triagent.Scoring;

public sealed class ClaudeCliScorer : CliScorerBase
{
    private readonly string _model;

    public ClaudeCliScorer(string model = "sonnet")
    {
        _model = model;
    }

    public override string Name => "claude";

    protected override string Executable => "claude";

    protected override IReadOnlyList<string> BuildArguments(string prompt)
        => ["-p", prompt, "--model", _model];
}
