using System.IO;

namespace SweaxMorph.Core;

/// <summary>
/// One thing a converter can do: turn <see cref="From"/> into <see cref="To"/>. Cost picks the best route
/// (lower is better: faster and more faithful). <see cref="Many"/> results are several files (pages, frames),
/// so they can only end a route. <see cref="Combines"/> edges take every dropped file at once.
/// </summary>
public readonly record struct Edge(string From, string To, int Cost, bool Many = false, bool Combines = false);

public sealed class Options
{
    public Quality Quality { get; init; } = Quality.Balanced;
    /// <summary>Several dropped files become one result (images → one PDF, files → one zip).</summary>
    public bool Combine { get; init; }
}

public sealed class Step
{
    public required string[] Inputs { get; init; }
    public string Input => Inputs[0];
    public required Format From { get; init; }
    public required Format To { get; init; }
    public required string OutDir { get; init; }
    public required string Name { get; init; }
    public required Options Options { get; init; }
    public required IProgress<double> Progress { get; init; }
    public CancellationToken Cancel { get; init; }
    public Quality Quality => Options.Quality;
    /// <summary>The usual result path inside the scratch folder.</summary>
    public string Out(string? ext = null) => Path.Combine(OutDir, Name + (ext ?? To.Ext));
}

public abstract class Converter
{
    public abstract string Name { get; }
    /// <summary>The outside program this needs, or null when it works on its own.</summary>
    public virtual Engine? Engine => null;
    public bool Ready => Engine?.Ready ?? true;
    public abstract IEnumerable<Edge> Edges();
    /// <summary>Converts and returns the files it made (all inside <see cref="Step.OutDir"/>).</summary>
    public abstract Task<string[]> Run(Step step);

    protected static IEnumerable<Edge> Cross(IEnumerable<string> from, IEnumerable<string> to, int cost, bool many = false)
    {
        var targets = to.ToArray();
        foreach (var f in from)
            foreach (var t in targets)
                if (f != t) yield return new Edge(f, t, cost, many);
    }
}

/// <summary>A friendly failure: the message is shown to the user as it is.</summary>
public sealed class ConvertException(string message) : Exception(message);

public sealed record Hop(Converter Converter, Edge Edge);

/// <summary>How to reach a format: one to three hops, and the engine that still has to be downloaded first, if any.</summary>
public sealed record Route(Format Target, IReadOnlyList<Hop> Hops, int Cost)
{
    public Engine? Missing => Hops.Select(h => h.Converter.Engine).FirstOrDefault(e => e is { Ready: false });
    public bool Many => Hops[^1].Edge.Many;
}

/// <summary>Finds the cheapest chain of converters from one format to every other.</summary>
public static class Planner
{
    static List<Converter> _converters = [];
    static Dictionary<string, List<Hop>> _out = [];
    static readonly Dictionary<string, Dictionary<string, Route>> Cache = [];

    public static IReadOnlyList<Converter> Converters => _converters;

    public static void Build(IEnumerable<Converter> converters)
    {
        _converters = converters.ToList();
        Rebuild();
        Engines.Changed += Rebuild;
    }

    public static void Rebuild()
    {
        var map = new Dictionary<string, List<Hop>>();
        foreach (var c in _converters)
        {
            // an engine we can't download and that isn't installed adds nothing
            if (!c.Ready && c.Engine is { Installable: false }) continue;
            foreach (var e in c.Edges())
            {
                if (Formats.Find(e.From) == null || Formats.Find(e.To) == null) continue;
                var edge = c.Ready ? e : e with { Cost = e.Cost + 6 }; // prefer what is already here
                if (!map.TryGetValue(e.From, out var list)) map[e.From] = list = [];
                list.Add(new Hop(c, edge));
            }
        }
        lock (Cache) { _out = map; Cache.Clear(); }
    }

    /// <summary>Every format <paramref name="from"/> can become, with the best route to each.</summary>
    public static IReadOnlyDictionary<string, Route> Targets(string from)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(from, out var hit)) return hit;
            var best = new Dictionary<string, Route>();
            var queue = new PriorityQueue<(string at, List<Hop> path, int cost), int>();
            queue.Enqueue((from, [], 0), 0);
            var settled = new HashSet<string>();
            while (queue.TryDequeue(out var item, out _))
            {
                if (!settled.Add(item.at)) continue;
                if (item.at != from) best[item.at] = new Route(Formats.Get(item.at), item.path, item.cost);
                if (item.path.Count >= 3 || item.path.Count > 0 && item.path[^1].Edge.Many) continue;
                // archives and folders end a route, they don't carry one: no "photo → zip → folder"
                if (item.path.Count > 0 && Formats.Get(item.at).Kind is Kind.Archive or Kind.Folder) continue;
                if (!_out.TryGetValue(item.at, out var hops)) continue;
                foreach (var hop in hops)
                {
                    if (hop.Edge.Combines || settled.Contains(hop.Edge.To) || hop.Edge.To == from) continue;
                    // every extra hop costs a little, so a direct conversion wins a tie
                    int cost = item.cost + hop.Edge.Cost + (item.path.Count > 0 ? 1 : 0);
                    queue.Enqueue((hop.Edge.To, [.. item.path, hop], cost), cost);
                }
            }
            Cache[from] = best;
            return best;
        }
    }

    /// <summary>Converters that make a smaller copy in the same format (shown as "Shrink").</summary>
    public static Hop? Shrink(string format)
    {
        lock (Cache)
            return _out.TryGetValue(format, out var hops) ? hops.Where(h => h.Edge.To == format).OrderBy(h => h.Edge.Cost).FirstOrDefault() : null;
    }

    /// <summary>A converter that takes all of these files at once and makes one <paramref name="to"/>.</summary>
    public static Hop? Combiner(IEnumerable<string> fromFormats, string to)
    {
        var froms = fromFormats.Distinct().ToList();
        lock (Cache)
        {
            Hop? pick = null;
            foreach (var f in froms)
            {
                var hop = _out.TryGetValue(f, out var hops) ? hops.Where(h => h.Edge.Combines && h.Edge.To == to).OrderBy(h => h.Edge.Cost).FirstOrDefault() : null;
                if (hop == null) return null;
                if (pick != null && pick.Converter != hop.Converter) return null;
                pick ??= hop;
            }
            return pick;
        }
    }

    /// <summary>Formats the given files can be combined into (one result for all of them).</summary>
    public static IEnumerable<string> CombineTargets(IReadOnlyCollection<string> fromFormats)
    {
        lock (Cache)
        {
            HashSet<string>? common = null;
            foreach (var f in fromFormats.Distinct())
            {
                var mine = _out.TryGetValue(f, out var hops) ? hops.Where(h => h.Edge.Combines).Select(h => h.Edge.To).ToHashSet() : [];
                if (common == null) common = mine;
                else common.IntersectWith(mine);
            }
            return common ?? [];
        }
    }
}
