using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NosAi.Core.Cognitive;

namespace NosAi.ControlPanel;

public partial class CognitiveMemoryWindow : Window
{
    private readonly ICognitiveObservabilityReader _cognitive;
    private readonly string _repoRoot;
    private readonly List<MemoryItem> _memory = new();

    public CognitiveMemoryWindow(ICognitiveObservabilityReader cognitive, string repoRoot)
    {
        InitializeComponent();
        _cognitive = cognitive ?? throw new ArgumentNullException(nameof(cognitive));
        _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
        RefreshAll();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshAll();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyMemoryFilter();

    private void RefreshAll()
    {
        var traces = _cognitive.GetRecentTrace(250);
        TraceList.ItemsSource = traces.Reverse().Select(ToTraceRow).ToArray();

        var decision = _cognitive.GetLatestDecision();
        DecisionText.Text = decision?.Status == "Committed"
            ? decision.SelectedAction
            : decision is null ? "UNKNOWN" : $"{decision.Status}: {decision.SelectedAction}";
        DecisionMeta.Text = decision is null
            ? "Nessuna decisione osservata dal trace."
            : $"Obiettivo: {decision.Objective} · Confidence {decision.Confidence:P0} · Risk {decision.Risk:P0} · Cycle {decision.CycleId}";
        CandidateList.ItemsSource = decision?.Candidates.Select(c => new CandidateRow(c)).ToArray() ?? Array.Empty<CandidateRow>();

        BuildMemoryIndex();
        BuildMemoryTree();
        ApplyMemoryFilter();
    }

    private void BuildMemoryTree()
    {
        var groups = _memory.GroupBy(x => x.Category).OrderBy(x => x.Key)
            .Select(g => new { Name = $"{IconFor(g.Key)}  {g.Key}", Count = g.Count(), Items = g.ToArray() }).ToArray();
        MemoryTree.ItemsSource = groups.Select(g => new TreeRow(g.Name, g.Count)).ToArray();
    }

    private void BuildMemoryIndex()
    {
        _memory.Clear();
        var roots = new[] { "data", "logs", "memory", "storage" };
        foreach (var root in roots)
        {
            var path = Path.Combine(_repoRoot, root);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    _memory.Add(new MemoryItem(CategoryFor(root, file), info.Name, Path.GetRelativePath(_repoRoot, file), info.Length, info.LastWriteTimeUtc, file));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void ApplyMemoryFilter()
    {
        var q = MemorySearch?.Text?.Trim() ?? string.Empty;
        var items = string.IsNullOrWhiteSpace(q)
            ? _memory
            : _memory.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        MemoryList.ItemsSource = items.OrderBy(x => x.Category).ThenBy(x => x.Name).Select(x => new MemoryRow(x, Inspect(x))).ToArray();
    }

    private static string Inspect(MemoryItem item)
        => $"NAME        {item.Name}\nCATEGORY    {item.Category}\nPATH        {item.Path}\nSIZE        {item.Length:N0} bytes\nMODIFIED    {item.ModifiedUtc:O}\n\nSTATUS\nPresent on disk. Content is not fabricated by the dashboard.\n\nSOURCE\nFilesystem observation from Control Panel.";

    private TraceRow ToTraceRow(CognitiveTraceEvent e)
    {
        var active = e.Status is CognitiveNodeStatus.Completed or CognitiveNodeStatus.Running;
        var background = active ? new SolidColorBrush(Color.FromRgb(18, 48, 42)) : new SolidColorBrush(Color.FromRgb(17, 26, 45));
        var border = active ? new SolidColorBrush(Color.FromRgb(55, 150, 112)) : new SolidColorBrush(Color.FromRgb(38, 52, 81));
        return new TraceRow(e.Node.ToString(), e.Summary, $"{e.EventType} · {e.OccurredAtUtc:HH:mm:ss.fff} · confidence {e.Confidence:P0}", e.Status.ToString(), background, border);
    }

    private static string CategoryFor(string root, string file) => root switch
    {
        "memory" => "Semantic / Episodic Memory",
        "storage" => "Persistent Runtime Data",
        "logs" => "Event & Audit Journal",
        _ => file.Contains("knowledge", StringComparison.OrdinalIgnoreCase) ? "Knowledge" : "Runtime Data"
    };

    private static string IconFor(string category) => category switch
    {
        "Semantic / Episodic Memory" => "🧠",
        "Knowledge" => "📚",
        "Event & Audit Journal" => "📜",
        "Persistent Runtime Data" => "💾",
        _ => "📦"
    };

    private sealed record MemoryItem(string Category, string Name, string Path, long Length, DateTime ModifiedUtc, string FullPath);
    private sealed record TraceRow(string Node, string Summary, string Detail, string Status, Brush Background, Brush Border);
    private sealed record CandidateRow(DecisionCandidateView Candidate)
    {
        public string Name => Candidate.Id;
        public string Action => Candidate.Action;
        public string Meta => $"score {Candidate.Score:F3} · risk {Candidate.Risk:P0} · confidence {Candidate.Confidence:P0} · {Candidate.Status}";
    }
    private sealed record TreeRow(string Name, int Count);
    private sealed record MemoryRow(MemoryItem Item, string Inspection)
    {
        public string Icon => Item.Category.Contains("Memory", StringComparison.OrdinalIgnoreCase) ? "🧠" : "📄";
        public string Name => Item.Name;
        public string Path => Item.Path;
        public string Kind => Item.Category;
    }
}
