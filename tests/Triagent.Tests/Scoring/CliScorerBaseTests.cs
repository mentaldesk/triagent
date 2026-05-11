using Triagent.Scoring;
using Xunit;

namespace Triagent.Tests.Scoring;

public class CliScorerBaseParseResponseTests
{
    private const string ValidArray = """
        [{"number":1,"specificity":3,"containment":2,"verifiability":2,"clarity":3,"self_sufficiency":3,"rationale":"clean bug"}]
        """;

    [Fact]
    public void Parses_plain_json_array()
    {
        var result = CliScorerBase.ParseResponse(ValidArray);

        Assert.Single(result);
        var score = result[0];
        Assert.Equal(1, score.Number);
        Assert.Equal(3, score.Specificity);
        Assert.Equal(2, score.Containment);
        Assert.Equal(2, score.Verifiability);
        Assert.Equal(3, score.Clarity);
        Assert.Equal(3, score.SelfSufficiency);
        Assert.Equal(13, score.Total);
        Assert.Equal("clean bug", score.Rationale);
    }

    [Fact]
    public void Strips_triple_backtick_fences_with_language_tag()
    {
        var raw = $"```json\n{ValidArray}\n```";
        var result = CliScorerBase.ParseResponse(raw);
        Assert.Single(result);
    }

    [Fact]
    public void Strips_triple_backtick_fences_without_language_tag()
    {
        var raw = $"```\n{ValidArray}\n```";
        var result = CliScorerBase.ParseResponse(raw);
        Assert.Single(result);
    }

    [Fact]
    public void Strips_leading_and_trailing_prose()
    {
        var raw = $"Sure, here's the analysis:\n\n{ValidArray}\n\nLet me know if you want more detail.";
        var result = CliScorerBase.ParseResponse(raw);
        Assert.Single(result);
        Assert.Equal(1, result[0].Number);
    }

    [Fact]
    public void Parses_multi_issue_array()
    {
        var raw = """
            [
              {"number":10,"specificity":1,"containment":1,"verifiability":1,"clarity":1,"self_sufficiency":1,"rationale":"vague"},
              {"number":11,"specificity":3,"containment":3,"verifiability":3,"clarity":3,"self_sufficiency":3,"rationale":"ideal"}
            ]
            """;

        var result = CliScorerBase.ParseResponse(raw);

        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].Total);
        Assert.Equal(15, result[1].Total);
    }

    [Fact]
    public void Tolerates_extra_unknown_fields()
    {
        var raw = """
            [{"number":1,"specificity":2,"containment":2,"verifiability":2,"clarity":2,"self_sufficiency":2,"rationale":"meh","extra":"ignored","confidence":0.7}]
            """;

        var result = CliScorerBase.ParseResponse(raw);

        Assert.Single(result);
        Assert.Equal(10, result[0].Total);
    }

    [Fact]
    public void Handles_missing_rationale_as_empty_string()
    {
        var raw = """
            [{"number":1,"specificity":2,"containment":2,"verifiability":2,"clarity":2,"self_sufficiency":2}]
            """;

        var result = CliScorerBase.ParseResponse(raw);

        Assert.Single(result);
        Assert.Equal("", result[0].Rationale);
    }

    [Fact]
    public void Throws_when_no_array_present()
    {
        var raw = "I cannot score these issues right now.";

        var ex = Assert.Throws<InvalidOperationException>(() => CliScorerBase.ParseResponse(raw));
        Assert.Contains("did not contain a JSON array", ex.Message);
    }

    [Fact]
    public void Throws_when_array_is_malformed_json()
    {
        var raw = "[{this is not valid json}]";

        Assert.ThrowsAny<Exception>(() => CliScorerBase.ParseResponse(raw));
    }

    [Fact]
    public void Returns_empty_list_for_empty_array()
    {
        var raw = "[]";

        var result = CliScorerBase.ParseResponse(raw);

        Assert.Empty(result);
    }
}
