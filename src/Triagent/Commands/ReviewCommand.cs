using System.Collections.ObjectModel;
using System.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Triagent.Config;
using Triagent.Data;
using Triagent.Models;

namespace Triagent.Commands;

public static class ReviewCommand
{
    public static Task<int> RunAsync(string? repo, TriagentConfig config, CancellationToken ct)
        => RunAsync(repo, config, onAppReady: null, ct);

    internal static async Task<int> RunAsync(string? repo, TriagentConfig config, Action<IApplication>? onAppReady, CancellationToken ct)
    {
        await using var db = await IssueDb.OpenAsync(config.ResolvedDbPath, ct);
        var allIssues = (await db.QueryReviewQueueAsync(repo, ct)).ToList();

        if (allIssues.Count == 0)
        {
            Console.WriteLine("No scored open issues available for review.");
            return 0;
        }

        List<IssueRow> visibleIssues = [.. allIssues];
        ReviewDecision? activeFilter = ParseFilter(config.ReviewFilter);
        bool modalOpen = false;

        using IApplication app = Application.Create();
        app.Init();

        using var win = new Window { Title = "Triagent Review" };

        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = 48,
            Height = Dim.Fill(1),
        };

        var details = new TextView
        {
            X = Pos.Right(list),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            ReadOnly = true,
            WordWrap = true,
        };

        var status = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Text = "Showing: All  |  ?/F1 Help   Ctrl+Q Quit",
        };

        win.Add(list, details, status);

        ObservableCollection<string> BuildListItems() => new(visibleIssues
            .Select(i => $"[{DecisionGlyph(i.ReviewDecision)}] {i.Total,2} #{i.Number} {Trim(i.Title, 30)}")
            .ToList());

        string FilterText() => activeFilter switch
        {
            null => "All",
            ReviewDecision.None => "Unreviewed",
            ReviewDecision.Skip => "Skip",
            ReviewDecision.Try => "Try",
            _ => "All",
        };

        static string FilterToConfigValue(ReviewDecision? filter) => filter switch
        {
            null => "all",
            ReviewDecision.None => "unreviewed",
            ReviewDecision.Skip => "skip",
            ReviewDecision.Try => "try",
            _ => "all",
        };

        void PersistFilter()
        {
            config.ReviewFilter = FilterToConfigValue(activeFilter);
            TriagentConfig.Save(config);
        }

        void RefreshList(string? preferredRepo = null, int preferredIssueNumber = -1)
        {
            visibleIssues = activeFilter switch
            {
                null => [.. allIssues],
                _ => allIssues.Where(i => i.ReviewDecision == activeFilter.Value).ToList(),
            };

            list.SetSource(BuildListItems());

            if (visibleIssues.Count == 0)
            {
                details.Text = $"No issues for filter: {FilterText()}";
                status.Text = $"Showing: {FilterText()}  |  ?/F1 Help   Ctrl+Q Quit";
                return;
            }

            var idx = 0;
            if (preferredIssueNumber > 0)
            {
                var found = visibleIssues.FindIndex(i => i.Number == preferredIssueNumber && (preferredRepo is null || i.Repo == preferredRepo));
                if (found >= 0) idx = found;
            }

            list.SelectedItem = idx;
            UpdateDetails(idx);
        }

        int CurrentIndex() => list.SelectedItem ?? 0;

        IssueRow? CurrentIssueOrNull()
        {
            var idx = CurrentIndex();
            return idx >= 0 && idx < visibleIssues.Count ? visibleIssues[idx] : null;
        }

        void UpdateDetails(int idx)
        {
            if (idx < 0 || idx >= visibleIssues.Count)
            {
                details.Text = $"No issues for filter: {FilterText()}";
                return;
            }

            var i = visibleIssues[idx];
            details.Text =
                $"{i.Repo}#{i.Number}\n" +
                $"{i.Title}\n\n" +
                $"Score: {i.Total?.ToString() ?? "-"} (S:{i.Specificity?.ToString() ?? "-"} C:{i.Containment?.ToString() ?? "-"} V:{i.Verifiability?.ToString() ?? "-"} Cl:{i.Clarity?.ToString() ?? "-"} SS:{i.SelfSufficiency?.ToString() ?? "-"})\n" +
                $"Labels: {i.Labels}\n" +
                $"URL: {i.Url}\n\n" +
                $"Rationale:\n{i.Rationale ?? "(none)"}\n\n" +
                $"Decision: {i.ReviewDecision.ToDisplay()}\n" +
                $"Reason: {i.ReviewReason ?? "(none)"}";

            status.Text = $"Showing: {FilterText()}  |  Selected #{i.Number}  |  ?/F1 Help   Ctrl+Q Quit";
        }

        void ShowHelp()
        {
            modalOpen = true;
            MessageBox.Query(
                app,
                "Shortcuts",
                "Issue actions:\n" +
                "O: Open in browser\n" +
                "S: Skip (reason required)\n" +
                "T: Mark Try\n" +
                "U: Mark Unreviewed\n\n" +
                "App actions:\n" +
                "Ctrl+Q: Quit\n" +
                "Ctrl+A: Show All (clear filter)\n" +
                "Ctrl+U: Filter Unreviewed\n" +
                "Ctrl+S: Filter Skip\n" +
                "Ctrl+T: Filter Try\n" +
                "? or F1: Show this help",
                "OK");
            modalOpen = false;
        }

        async Task SetDecisionAsync(ReviewDecision decision, string? reason, string issueRepo, int number)
        {
            await db.SaveReviewDecisionAsync(issueRepo, number, decision, reason, ct);

            var persisted = await db.GetAsync(issueRepo, number, ct);
            if (persisted is null)
            {
                status.Text = $"Failed to persist decision for {issueRepo}#{number} (row not found).";
                return;
            }

            var idx = allIssues.FindIndex(i => i.Repo == issueRepo && i.Number == number);
            if (idx >= 0)
                allIssues[idx] = persisted;

            status.Text = $"Saved: {issueRepo}#{number} -> {persisted.ReviewDecision.ToDisplay()}";
            RefreshList(issueRepo, number);
        }

        async Task MarkTryAsync()
        {
            var current = CurrentIssueOrNull();
            if (current is null) return;
            await SetDecisionAsync(ReviewDecision.Try, null, current.Repo, current.Number);
        }

        async Task MarkUnreviewedAsync()
        {
            var current = CurrentIssueOrNull();
            if (current is null) return;
            await SetDecisionAsync(ReviewDecision.None, null, current.Repo, current.Number);
        }

        void OpenInBrowser()
        {
            var current = CurrentIssueOrNull();
            if (current is null) return;

            var copied = TryCopyToClipboardOsc52(current.Url);
            var isSsh = IsSshSession();

            if (!isSsh)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = current.Url,
                        UseShellExecute = true,
                    });
                    status.Text = copied
                        ? "Opened in browser and copied URL to clipboard."
                        : "Opened in browser.";
                    return;
                }
                catch
                {
                    // Fall through and at least show URL + copy status.
                }
            }

            status.Text = copied
                ? $"Copied URL for #{current.Number}. Paste in your local browser."
                : $"Open URL manually: {current.Url}";
        }

        async Task ShowSkipDialogAsync()
        {
            var current = CurrentIssueOrNull();
            if (current is null) return;

            modalOpen = true;

            var reasonField = new TextField
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Text = current.ReviewReason ?? string.Empty,
            };

            var ok = new Button { Text = "OK" };
            var cancel = new Button { Text = "Cancel" };

            using var dlg = new Dialog { Title = $"Skip #{current.Number}" };
            dlg.Width = 70;
            dlg.Height = 8;

            dlg.Add(new Label
            {
                X = 0,
                Y = 0,
                Text = "Reason for skipping:",
            });
            reasonField.Y = 1;
            dlg.Add(reasonField);

            ok.X = 0;
            ok.Y = 3;
            cancel.X = Pos.Right(ok) + 1;
            cancel.Y = 3;
            dlg.Add(ok, cancel);

            var completed = false;

            async Task ConfirmAsync()
            {
                var text = reasonField.Text?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.ErrorQuery(app, "Validation", "Skip requires a reason.", "OK");
                    return;
                }

                await SetDecisionAsync(ReviewDecision.Skip, text, current.Repo, current.Number);
                completed = true;
                app.RequestStop(dlg);
            }

            ok.Accepting += async (_, e) =>
            {
                await ConfirmAsync();
                e.Handled = true;
            };

            cancel.Accepting += (_, e) =>
            {
                completed = true;
                app.RequestStop(dlg);
                e.Handled = true;
            };

            reasonField.KeyDown += async (_, k) =>
            {
                if (k.KeyCode == KeyCode.Esc)
                {
                    completed = true;
                    app.RequestStop(dlg);
                    k.Handled = true;
                }
                else if (k.KeyCode == KeyCode.Enter)
                {
                    await ConfirmAsync();
                    k.Handled = true;
                }
            };

            dlg.KeyDown += async (_, k) =>
            {
                if (k.KeyCode == KeyCode.Esc)
                {
                    completed = true;
                    app.RequestStop(dlg);
                    k.Handled = true;
                }
                else if (k.KeyCode == KeyCode.Enter)
                {
                    await ConfirmAsync();
                    k.Handled = true;
                }
            };

            app.Run(dlg);

            if (!completed)
                app.RequestStop(dlg);

            modalOpen = false;
            var idx = CurrentIndex();
            UpdateDetails(idx);
        }

        list.ValueChanged += (_, e) =>
        {
            var idx = e.NewValue ?? 0;
            UpdateDetails(idx);
        };

        async Task<bool> HandleShortcutAsync(Key k)
        {
            if (modalOpen) return false;

            var baseKey = k.NoShift.NoAlt.NoCtrl.KeyCode;

            if (k.IsCtrl && !k.IsAlt && k.NoCtrl.KeyCode == KeyCode.Q)
            {
                app.RequestStop();
                return true;
            }

            if (k.IsCtrl && !k.IsAlt && k.NoCtrl.KeyCode == KeyCode.A)
            {
                activeFilter = null;
                PersistFilter();
                RefreshList();
                return true;
            }

            if (k.IsCtrl && !k.IsAlt && k.NoCtrl.KeyCode == KeyCode.U)
            {
                activeFilter = ReviewDecision.None;
                PersistFilter();
                RefreshList();
                return true;
            }

            if (k.IsCtrl && !k.IsAlt && k.NoCtrl.KeyCode == KeyCode.S)
            {
                activeFilter = ReviewDecision.Skip;
                PersistFilter();
                RefreshList();
                return true;
            }

            if (k.IsCtrl && !k.IsAlt && k.NoCtrl.KeyCode == KeyCode.T)
            {
                activeFilter = ReviewDecision.Try;
                PersistFilter();
                RefreshList();
                return true;
            }

            if (baseKey == KeyCode.F1)
            {
                ShowHelp();
                return true;
            }

            var rune = k.AsRune.Value;
            if (rune == '?')
            {
                ShowHelp();
                return true;
            }

            if (!k.IsCtrl && !k.IsAlt && baseKey == KeyCode.O)
            {
                OpenInBrowser();
                return true;
            }

            if (!k.IsCtrl && !k.IsAlt && baseKey == KeyCode.S)
            {
                await ShowSkipDialogAsync();
                return true;
            }

            if (!k.IsCtrl && !k.IsAlt && baseKey == KeyCode.T)
            {
                await MarkTryAsync();
                return true;
            }

            if (!k.IsCtrl && !k.IsAlt && baseKey == KeyCode.U)
            {
                await MarkUnreviewedAsync();
                return true;
            }

            return false;
        }

        app.Keyboard.KeyDown += async (_, k) =>
        {
            if (await HandleShortcutAsync(k))
                k.Handled = true;
        };

        onAppReady?.Invoke(app);

        RefreshList();
        app.Run(win);
        return 0;
    }

    private static ReviewDecision? ParseFilter(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "unreviewed" => ReviewDecision.None,
        "skip" => ReviewDecision.Skip,
        "try" => ReviewDecision.Try,
        _ => null,
    };

    private static bool IsSshSession() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SSH_CONNECTION")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SSH_CLIENT")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SSH_TTY"));

    private static bool TryCopyToClipboardOsc52(string text)
    {
        try
        {
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            Console.Write($"\u001b]52;c;{b64}\u0007");
            Console.Out.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static char DecisionGlyph(ReviewDecision decision) => decision switch
    {
        ReviewDecision.Try => 'T',
        ReviewDecision.Skip => 'S',
        _ => ' ',
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
