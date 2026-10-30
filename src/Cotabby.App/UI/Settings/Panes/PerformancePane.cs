using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Settings.Panes;

internal static class PerformancePane
{
    private sealed record SourceChoice(ModelRequestSource Value)
    {
        public override string ToString() => InterfaceText.Get(Value.Label());
    }
    public static SettingsPane Build(Rows r)
    {
        var context = r.Context;
        var metrics = context.Host.Coordinator.Metrics;
        var history = context.Host.Speech.History;
        var resourceStatus = Ui.Text(InterfaceText.Get("Collecting…", "Localizable"), "Caption");
        var cpuTrend = new Polyline { Height = 55, Stretch = Stretch.Fill, StrokeThickness = 1.5 }.Themed(Shape.StrokeProperty, "AccentBrush");
        var memoryTrend = new Polyline { Height = 55, Stretch = Stretch.Fill, StrokeThickness = 1.5 }.Themed(Shape.StrokeProperty, "AccentBrush");
        var cpuSamples = new Queue<double>(); var memorySamples = new Queue<double>();
        var resources = new StackPanel { Children = { Ui.Text(InterfaceText.Get("CPU", "Localizable"), "Caption"), cpuTrend, Ui.Text(InterfaceText.Get("Memory", "Localizable"), "Caption"), memoryTrend, resourceStatus } };
        var resourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        TimeSpan? priorCpu = null;
        long priorTime = 0;
        resourceTimer.Tick += (_, _) =>
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                long now = Stopwatch.GetTimestamp();
                string cpuText = priorCpu is { } before && now > priorTime
                    ? $"{(cpu - before).TotalSeconds / Stopwatch.GetElapsedTime(priorTime, now).TotalSeconds * 100:F0}%" : "—";
                if (priorCpu is { } previous && now > priorTime)
                {
                    cpuSamples.Enqueue((cpu - previous).TotalSeconds / Stopwatch.GetElapsedTime(priorTime, now).TotalSeconds * 100);
                    if (cpuSamples.Count > 60) cpuSamples.Dequeue();
                    cpuTrend.Points = [.. cpuSamples.Select((value, index) => new Point(index, -Math.Max(0, value)))];
                }
                memorySamples.Enqueue(process.WorkingSet64 / (1024.0 * 1024));
                if (memorySamples.Count > 60) memorySamples.Dequeue();
                memoryTrend.Points = [.. memorySamples.Select((value, index) => new Point(index, -value))];
                priorCpu = cpu; priorTime = now;
                resourceStatus.Text = $"CPU {cpuText} · Memory {process.WorkingSet64 / (1024.0 * 1024):F0} MB · GPU unavailable";
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            { resourceStatus.Text = InterfaceText.Get("Resource readings are unavailable.", "Localizable"); }
        };
        resources.IsVisibleChanged += (_, _) =>
        {
            if (resources.IsVisible) { priorCpu = null; resourceTimer.Start(); }
            else { resourceTimer.Stop(); priorCpu = null; }
        };
        context.Closed += resourceTimer.Stop;
        var tracking = new CheckBox { Content = InterfaceText.Get("Enable Performance Tracking"), IsChecked = context.Settings.PerformanceTrackingEnabled, IsEnabled = history is not null };
        var trackingError = Ui.Text("", "Caption");
        tracking.Click += (_, _) =>
        {
            bool previous = context.Settings.PerformanceTrackingEnabled;
            context.Settings.PerformanceTrackingEnabled = tracking.IsChecked == true;
            try
            {
                context.Host.SettingsStore.Save(context.Settings);
                context.Host.Speech.HistoryCollector?.SetEnabled(context.Settings.PerformanceTrackingEnabled);
                trackingError.Text = "";
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                context.Settings.PerformanceTrackingEnabled = previous; tracking.IsChecked = previous;
                trackingError.Text = InterfaceText.Get("Could not save the tracking preference.", "Localizable");
            }
        };
        context.OnRefresh(() => tracking.IsChecked = context.Settings.PerformanceTrackingEnabled);
        var source = new ComboBox { ItemsSource = ModelRequestSources.Current.Select(value => new SourceChoice(value)).ToArray(), SelectedIndex = 0, MinWidth = 180 };
        var records = new StackPanel();
        var clear = Ui.Button(InterfaceText.Get("Clear All"), () => history?.Clear());
        void UpdateHistory()
        {
            records.Children.Clear();
            if (history is null) { records.Children.Add(Ui.Text(InterfaceText.Get("Request history is unavailable.", "Localizable"), "Caption")); return; }
            var selected = ((SourceChoice)source.SelectedItem).Value;
            var entries = history.Entries.Where(x => x.Source == selected).Reverse().ToArray();
            clear.IsEnabled = history.Entries.Count != 0;
            if (entries.Length == 0) records.Children.Add(Ui.Text(context.Settings.PerformanceTrackingEnabled ? "No requests recorded for this task yet." : "Performance tracking is off.", "Caption"));
            else if (entries.Length > 1)
            {
                var trend = new Polyline { Height = 70, Stretch = Stretch.Fill, StrokeThickness = 1.5,
                    Points = [.. entries.Reverse().Select((entry, index) => new Point(index, -Math.Max(0, entry.LatencyMs)))] }
                    .Themed(Shape.StrokeProperty, "AccentBrush");
                records.Children.Add(trend);
            }
            foreach (var entry in entries)
            {
                var stages = new[] { ("Load", entry.ModelLoadMs), (entry.Source == ModelRequestSource.Speech ? "First audio" : "First output", entry.FirstOutputMs), (entry.Source == ModelRequestSource.Speech ? "Generation completed" : "First usable", entry.UsableResultMs), (entry.Source == ModelRequestSource.Speech ? "Playback started" : "Displayed", entry.DisplayMs) };
                var details = string.Join(" · ", stages.Where(x => x.Item2.HasValue).Select(x => $"{x.Item1} {x.Item2} ms"));
                records.Children.Add(Ui.Text($"{entry.Timestamp.ToLocalTime():T}   {entry.ModelName}   {entry.LatencyMs} ms\n{InterfaceText.Get(entry.Source.Label())} · {InterfaceText.Get(entry.Outcome.Label())}" + (details.Length > 0 ? "\n" + details : ""), "Caption"));
            }
        }
        source.SelectionChanged += (_, _) => UpdateHistory();
        tracking.Click += (_, _) => UpdateHistory();
        bool historyUpdateQueued = false, historyClosed = false;
        if (history is not null) { history.PropertyChanged += OnHistoryChanged; context.Closed += () => { historyClosed = true; history.PropertyChanged -= OnHistoryChanged; }; }
        void OnHistoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (historyClosed || historyUpdateQueued) return;
            historyUpdateQueued = true;
            records.Dispatcher.BeginInvoke(() => { historyUpdateQueued = false; if (!historyClosed) UpdateHistory(); });
        }
        context.OnRefresh(UpdateHistory);

        TextBlock shown = Value(), accepted = Value(), rate = Value(), words = Value();
        var latency = Ui.Text("", "Caption");
        var sparkline = new Polyline { Stretch = Stretch.Fill, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round }
            .Themed(Shape.StrokeProperty, "AccentBrush");
        var chart = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 12),
            Children = { new Border { Height = 56, Child = sparkline }, latency },
        };
        latency.Margin = new Thickness(0, 8, 0, 0);

        void Update()
        {
            long shownCount = metrics.SuggestionsShown;
            shown.Text = $"{shownCount:N0}";
            accepted.Text = $"{metrics.SuggestionsAccepted:N0}";
            rate.Text = shownCount == 0 ? "-" : $"{(double)metrics.SuggestionsAccepted / shownCount:P0}";
            words.Text = $"{metrics.WordsAccepted:N0}";

            var samples = metrics.LatenciesMs;
            sparkline.Points = [.. samples.Select((ms, index) => new Point(index, -ms))];
            latency.Text = samples.Count == 0
                ? InterfaceText.Get("No suggestions yet. Type somewhere to see how long they take.", "Localizable")
                : $"Last {samples[^1]} ms · Median {samples.Order().ElementAt(samples.Count / 2)} ms · "
                    + $"{samples.Count} most recent suggestions";
        }

        // The metrics change off the UI thread, and only a visible pane needs to follow them.
        void OnMetricsChanged() => chart.Dispatcher.BeginInvoke(Update);
        chart.IsVisibleChanged += (_, _) =>
        {
            metrics.Changed -= OnMetricsChanged;
            if (!chart.IsVisible) return;
            metrics.Changed += OnMetricsChanged;
            Update();
        };
        context.Closed += () => metrics.Changed -= OnMetricsChanged;

        return new SettingsPane("Performance", "\uE9D9", "Suggestion quality and latency",
            new SettingsCard("Live Resource Usage",
                r.Stacked("This process", "Updated every second while this pane is visible. Helper processes and GPU counters are not included.", resources)),
            new SettingsCard("Suggestion Quality",
                r.Labeled("Suggestions Shown", "How many suggestions appeared since Lingrove started.", shown)
                    .Tagged("quality stats counters"),
                r.Labeled("Suggestions Accepted", "How many of them you accepted at least part of.", accepted)
                    .Tagged("quality stats counters"),
                r.Labeled("Acceptance Rate", "The share of shown suggestions you accepted.", rate)
                    .Tagged("quality stats percent"),
                r.Labeled("Words Completed", "Words you have accepted from suggestions so far.", words)
                    .Tagged("word count lifetime total")),
            new SettingsCard("Request history",
                r.Labeled("Tracking", "Record timestamps, models and elapsed time. The latest 100 requests are retained across tasks.", tracking),
                Rows.Custom(trackingError),
                r.Labeled("Request type", "Durations are shown separately for each task.", source),
                r.Labeled("Clear history", "Remove all retained request records.", clear),
                r.Stacked("Recent requests", "Only connected task observers can add records.", records)),
            new SettingsCard("Latency",
                r.Stacked("Latency", "How long recent suggestions took to appear.", chart)
                    .Tagged("speed timing duration recent requests chart graph milliseconds")));
    }

    private static TextBlock Value() => Ui.Text("", "Mono");
}
