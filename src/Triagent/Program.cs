using System.CommandLine;
using Spectre.Console;
using Triagent.Commands;
using Triagent.Config;

var config = TriagentConfig.Load();

var root = new RootCommand("Triagent — score GitHub issues for AI-agent doability");

// Shared options
var dbOption = new Option<string?>("--db") { Description = "Override database path." };
root.Add(dbOption);

// ---- analyze ----
var analyzeCommand = new Command("analyze", "Fetch open issues and score new/changed ones.");
var analyzeRepoArg = new Argument<string>("repo")
{
    Description = "owner/name of the repository to analyze.",
    Arity = ArgumentArity.ZeroOrOne,
};
var analyzeScorerOpt = new Option<string>("--scorer") { Description = "Scorer to use: claude | codex.", DefaultValueFactory = _ => config.DefaultScorer };
var analyzeModelOpt = new Option<string>("--model") { Description = "Model for the scorer (e.g. sonnet, opus, gpt-5).", DefaultValueFactory = _ => config.DefaultModel };
var analyzeBatchOpt = new Option<int>("--batch-size") { Description = "Issues per scorer call.", DefaultValueFactory = _ => config.BatchSize };
var analyzeForceOpt = new Option<bool>("--force") { Description = "Re-score every issue regardless of timestamps." };

analyzeCommand.Add(analyzeRepoArg);
analyzeCommand.Add(analyzeScorerOpt);
analyzeCommand.Add(analyzeModelOpt);
analyzeCommand.Add(analyzeBatchOpt);
analyzeCommand.Add(analyzeForceOpt);

analyzeCommand.SetAction(async (parseResult, ct) =>
{
    var repo = parseResult.GetValue(analyzeRepoArg) ?? config.DefaultRepo;
    if (string.IsNullOrWhiteSpace(repo))
    {
        AnsiConsole.MarkupLine("[red]No repo specified and no defaultRepo in config.[/]");
        return 1;
    }
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    return await AnalyzeCommand.RunAsync(
        repo,
        parseResult.GetValue(analyzeScorerOpt)!,
        parseResult.GetValue(analyzeModelOpt)!,
        parseResult.GetValue(analyzeBatchOpt),
        parseResult.GetValue(analyzeForceOpt),
        config,
        ct);
});

// ---- top ----
var topCommand = new Command("top", "Print the highest-scoring issues.");
var topLimitArg = new Argument<int>("limit") { DefaultValueFactory = _ => 10, Arity = ArgumentArity.ZeroOrOne };
var topRepoOpt = new Option<string?>("--repo") { Description = "Filter to a single repo (defaults to config.defaultRepo)." };
var topStateOpt = new Option<string>("--state") { Description = "open | closed | all", DefaultValueFactory = _ => "open" };
var topMinOpt = new Option<int?>("--min-score") { Description = "Only show issues with total >= this." };
var topLabelOpt = new Option<string?>("--label") { Description = "Filter by label substring." };
var topExcludeAssignedOpt = new Option<bool>("--exclude-assigned") { Description = "Exclude issues that currently have one or more assignees." };
topCommand.Add(topLimitArg);
topCommand.Add(topRepoOpt);
topCommand.Add(topStateOpt);
topCommand.Add(topMinOpt);
topCommand.Add(topLabelOpt);
topCommand.Add(topExcludeAssignedOpt);
topCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;
    var repo = parseResult.GetValue(topRepoOpt) ?? config.DefaultRepo;
    return await TopCommand.RunAsync(
        parseResult.GetValue(topLimitArg),
        repo,
        parseResult.GetValue(topStateOpt)!,
        parseResult.GetValue(topMinOpt),
        parseResult.GetValue(topLabelOpt),
        parseResult.GetValue(topExcludeAssignedOpt),
        config,
        ct);
});

// ---- show ----
var showCommand = new Command("show", "Show full detail for one issue.");
var showNumberArg = new Argument<int>("number");
var showRepoOpt = new Option<string?>("--repo");
showCommand.Add(showNumberArg);
showCommand.Add(showRepoOpt);
showCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;
    var repo = parseResult.GetValue(showRepoOpt) ?? config.DefaultRepo;
    return await ShowCommand.RunAsync(parseResult.GetValue(showNumberArg), repo, config, ct);
});

// ---- stats ----
var statsCommand = new Command("stats", "Print summary stats for the local DB.");
var statsRepoOpt = new Option<string?>("--repo");
var statsIncludeClosedOpt = new Option<bool>("--include-closed");
statsCommand.Add(statsRepoOpt);
statsCommand.Add(statsIncludeClosedOpt);
statsCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;
    var repo = parseResult.GetValue(statsRepoOpt) ?? config.DefaultRepo;
    return await StatsCommand.RunAsync(repo, parseResult.GetValue(statsIncludeClosedOpt), config, ct);
});

// ---- export ----
var exportCommand = new Command("export", "Export the DB to TSV/CSV/JSON.");
var exportRepoOpt = new Option<string?>("--repo");
var exportFormatOpt = new Option<string>("--format") { Description = "tsv | csv | json", DefaultValueFactory = _ => "tsv" };
var exportOutputOpt = new Option<string?>("--output");
var exportIncludeClosedOpt = new Option<bool>("--include-closed");
exportCommand.Add(exportRepoOpt);
exportCommand.Add(exportFormatOpt);
exportCommand.Add(exportOutputOpt);
exportCommand.Add(exportIncludeClosedOpt);
exportCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;
    var repo = parseResult.GetValue(exportRepoOpt) ?? config.DefaultRepo;
    return await ExportCommand.RunAsync(
        repo,
        parseResult.GetValue(exportFormatOpt)!,
        parseResult.GetValue(exportOutputOpt),
        parseResult.GetValue(exportIncludeClosedOpt),
        config,
        ct);
});

// ---- repos ----
var reposCommand = new Command("repos", "List repos tracked in the DB.");
reposCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;
    return await ReposCommand.RunAsync(config, ct);
});

// ---- review ----
var reviewCommand = new Command("review", "Launch interactive triage review UI.");
var reviewRepoOpt = new Option<string?>("--repo") { Description = "Filter review queue to a single repo." };
reviewCommand.Add(reviewRepoOpt);
reviewCommand.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    var repo = parseResult.GetValue(reviewRepoOpt) ?? config.DefaultRepo;
    return await ReviewCommand.RunAsync(repo, config, ct);
});

// ---- linked-repos ----
var linkedReposCommand = new Command("linked-repos", "Manage repo-scoped linked PR repos used by analyze.");

var linkedList = new Command("list", "Show linked PR repos for a target repo.");
var linkedSet = new Command("set", "Replace linked PR repos for a target repo.");
var linkedAdd = new Command("add", "Add one or more linked PR repos for a target repo.");
var linkedRemove = new Command("remove", "Remove one or more linked PR repos for a target repo.");

var linkedRepoOpt = new Option<string?>("--repo") { Description = "Target analyzed repo (defaults to config.defaultRepo)." };
var linkedReposArg = new Argument<string[]>("repos")
{
    Description = "One or more owner/name repos.",
    Arity = ArgumentArity.OneOrMore,
};

linkedList.Add(linkedRepoOpt);
linkedSet.Add(linkedRepoOpt);
linkedSet.Add(linkedReposArg);
linkedAdd.Add(linkedRepoOpt);
linkedAdd.Add(linkedReposArg);
linkedRemove.Add(linkedRepoOpt);
linkedRemove.Add(linkedReposArg);

linkedList.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    var repo = parseResult.GetValue(linkedRepoOpt) ?? config.DefaultRepo;
    return await LinkedReposCommand.ListAsync(repo, config, ct);
});

linkedSet.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    var repo = parseResult.GetValue(linkedRepoOpt) ?? config.DefaultRepo;
    var repos = parseResult.GetValue(linkedReposArg) ?? Array.Empty<string>();
    return await LinkedReposCommand.SetAsync(repo, repos, config, ct);
});

linkedAdd.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    var repo = parseResult.GetValue(linkedRepoOpt) ?? config.DefaultRepo;
    var repos = parseResult.GetValue(linkedReposArg) ?? Array.Empty<string>();
    return await LinkedReposCommand.AddAsync(repo, repos, config, ct);
});

linkedRemove.SetAction(async (parseResult, ct) =>
{
    var dbOverride = parseResult.GetValue(dbOption);
    if (dbOverride is not null) config.DbPath = dbOverride;

    var repo = parseResult.GetValue(linkedRepoOpt) ?? config.DefaultRepo;
    var repos = parseResult.GetValue(linkedReposArg) ?? Array.Empty<string>();
    return await LinkedReposCommand.RemoveAsync(repo, repos, config, ct);
});

linkedReposCommand.Add(linkedList);
linkedReposCommand.Add(linkedSet);
linkedReposCommand.Add(linkedAdd);
linkedReposCommand.Add(linkedRemove);

root.Add(analyzeCommand);
root.Add(topCommand);
root.Add(showCommand);
root.Add(statsCommand);
root.Add(exportCommand);
root.Add(reposCommand);
root.Add(reviewCommand);
root.Add(linkedReposCommand);

return await root.Parse(args).InvokeAsync();
