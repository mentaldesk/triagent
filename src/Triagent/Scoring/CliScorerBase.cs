using System.Diagnostics;
using Sentry;
using System.Text.Json;
using System.Text.Json.Serialization;
using Triagent.Models;

namespace Triagent.Scoring;

/// <summary>
/// Shared plumbing for scorers that shell out to a CLI binary. Subclasses just say
/// what executable + arguments to invoke for a given prompt.
/// </summary>
public abstract class CliScorerBase : IScorer
{
    public abstract string Name { get; }

    protected abstract string Executable { get; }

    protected abstract IReadOnlyList<string> BuildArguments(string prompt);

    public async Task<IReadOnlyList<IssueScore>> ScoreBatchAsync(
        IReadOnlyList<IssueForScoring> batch,
        CancellationToken ct)
    {
        var span = SentrySdk.GetSpan()?.StartChild("scorer.batch", "Score batch");
        span?.SetTag("scorer", Name);
        span?.SetData("batch_count", batch.Count);

        var prompt = ScorePrompt.Build(batch);
        span?.SetData("prompt_chars", prompt.Length);

        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var attemptSpan = span?.StartChild("scorer.attempt", $"Scorer attempt {attempt + 1}");
            attemptSpan?.SetData("attempt", attempt + 1);
            try
            {
                var output = await RunAsync(prompt, ct);
                var parsed = ParseResponse(output);
                attemptSpan?.SetData("scores_returned", parsed.Count);
                attemptSpan?.Finish(SpanStatus.Ok);
                span?.Finish(SpanStatus.Ok);
                return parsed;
            }
            catch (Exception ex) when (attempt < 2)
            {
                attemptSpan?.SetTag("error", "true");
                attemptSpan?.Finish(SpanStatus.InternalError);
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        span?.SetTag("error", "true");
        span?.Finish(SpanStatus.InternalError);
        throw new InvalidOperationException(
            $"{Name} scorer failed after 3 attempts (promptChars={prompt.Length}): {lastError?.Message}", lastError);
    }

    private async Task<string> RunAsync(string prompt, CancellationToken ct)
    {
        var span = SentrySdk.GetSpan()?.StartChild("scorer.exec", "Execute scorer CLI");
        span?.SetTag("executable", Executable);

        var psi = new ProcessStartInfo
        {
            FileName = Executable,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in BuildArguments(prompt))
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {Executable}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        span?.SetData("exit_code", proc.ExitCode);

        if (proc.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(stderr) ? "(empty)" : stderr.Trim();
            var outSnippet = string.IsNullOrWhiteSpace(stdout)
                ? "(empty)"
                : stdout.Trim()[..Math.Min(400, stdout.Trim().Length)];

            span?.SetTag("error", "true");
            span?.SetData("stderr", err);
            span?.SetData("stdout_snippet", outSnippet);
            span?.Finish(SpanStatus.InternalError);

            throw new InvalidOperationException(
                $"{Executable} exited {proc.ExitCode}. stderr={err} stdout={outSnippet}");
        }

        span?.SetData("stdout_length", stdout.Length);
        span?.Finish(SpanStatus.Ok);
        return stdout;
    }

    internal static IReadOnlyList<IssueScore> ParseResponse(string raw)
    {
        var json = raw.Trim();

        // Strip ``` fences if the model added them despite instructions
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline > 0) json = json[(firstNewline + 1)..];
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0) json = json[..lastFence];
            json = json.Trim();
        }

        // The model may include prose before the array; find the array bounds.
        var firstBracket = json.IndexOf('[');
        var lastBracket = json.LastIndexOf(']');
        if (firstBracket < 0 || lastBracket <= firstBracket)
            throw new InvalidOperationException($"Scorer response did not contain a JSON array. Raw: {raw[..Math.Min(200, raw.Length)]}");

        json = json[firstBracket..(lastBracket + 1)];

        var parsed = JsonSerializer.Deserialize(json, ScoreJsonContext.Default.ListScoreDto)
            ?? throw new InvalidOperationException("Failed to deserialize scorer response.");

        return parsed.Select(s => new IssueScore(
            s.Number,
            s.Specificity,
            s.Containment,
            s.Verifiability,
            s.Clarity,
            s.SelfSufficiency,
            s.Rationale ?? "")).ToList();
    }
}

internal sealed class ScoreDto
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("specificity")] public int Specificity { get; set; }
    [JsonPropertyName("containment")] public int Containment { get; set; }
    [JsonPropertyName("verifiability")] public int Verifiability { get; set; }
    [JsonPropertyName("clarity")] public int Clarity { get; set; }
    [JsonPropertyName("self_sufficiency")] public int SelfSufficiency { get; set; }
    [JsonPropertyName("rationale")] public string? Rationale { get; set; }
}

[JsonSerializable(typeof(List<ScoreDto>))]
internal partial class ScoreJsonContext : JsonSerializerContext;
