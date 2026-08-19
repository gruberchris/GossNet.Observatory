namespace GossNet.Observatory.Cluster;

/// <summary>
/// The shape of the neighbour graph wired into each node's static discovery list.
/// </summary>
internal enum TopologyKind
{
    /// <summary>Every node knows every other node. Floods at O(N^2) datagrams per message.</summary>
    Mesh,

    /// <summary>Each node knows its two ring neighbours. Deep propagation trees, minimal waste.</summary>
    Ring,

    /// <summary>A ring backbone plus random chords: short paths without mesh-scale duplication.</summary>
    KRandom,

    /// <summary>Four-connected grid. Propagation visibly spreads as a wavefront.</summary>
    Grid
}

/// <summary>
/// Builds neighbour lists. Every topology is symmetric: if A lists B then B lists A.
/// </summary>
/// <remarks>
/// Asymmetric wiring silently breaks gossip — a node that nobody lists still receives
/// and forwards, but nothing ever reaches it first, so it looks alive while being
/// unreachable.
/// </remarks>
internal static class Topology
{
    /// <summary>
    /// Produces the neighbour indices for each of <paramref name="count"/> nodes.
    /// </summary>
    /// <param name="kind">The graph shape.</param>
    /// <param name="count">Number of nodes.</param>
    /// <param name="degree">Extra random chords per node, used by <see cref="TopologyKind.KRandom"/>.</param>
    /// <param name="seed">Seed for the random chords, so a run is reproducible.</param>
    public static IReadOnlyList<IReadOnlyList<int>> Build(TopologyKind kind, int count, int degree = 2, int seed = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);

        var adjacency = new HashSet<int>[count];

        for (var i = 0; i < count; i++)
        {
            adjacency[i] = [];
        }

        switch (kind)
        {
            case TopologyKind.Mesh:
                BuildMesh(adjacency, count);
                break;

            case TopologyKind.Ring:
                BuildRing(adjacency, count);
                break;

            case TopologyKind.KRandom:
                // The ring guarantees the graph is connected; the chords cut the diameter.
                BuildRing(adjacency, count);
                AddRandomChords(adjacency, count, degree, seed);
                break;

            case TopologyKind.Grid:
                BuildGrid(adjacency, count);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown topology.");
        }

        var result = new List<int>[count];

        for (var i = 0; i < count; i++)
        {
            var neighbours = new List<int>(adjacency[i]);
            neighbours.Sort();
            result[i] = neighbours;
        }

        return result;
    }

    private static void BuildMesh(HashSet<int>[] adjacency, int count)
    {
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i != j)
                {
                    adjacency[i].Add(j);
                }
            }
        }
    }

    private static void BuildRing(HashSet<int>[] adjacency, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Link(adjacency, i, (i + 1) % count);
        }
    }

    private static void AddRandomChords(HashSet<int>[] adjacency, int count, int degree, int seed)
    {
        var random = new Random(seed);

        for (var i = 0; i < count; i++)
        {
            for (var added = 0; added < degree; added++)
            {
                // Bounded retries: in a small cluster the ring may already cover
                // everything, and an unbounded search would spin.
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var candidate = random.Next(count);

                    if (candidate != i && adjacency[i].Add(candidate))
                    {
                        adjacency[candidate].Add(i);
                        break;
                    }
                }
            }
        }
    }

    private static void BuildGrid(HashSet<int>[] adjacency, int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            if (column + 1 < columns && i + 1 < count)
            {
                Link(adjacency, i, i + 1);
            }

            if (i + columns < count)
            {
                Link(adjacency, i, i + columns);
            }

            // A trailing partial row can leave the last cell with no right/down link;
            // the down-link from the row above already covers it, except when the grid
            // is a single row.
            _ = row;
        }
    }

    private static void Link(HashSet<int>[] adjacency, int a, int b)
    {
        if (a == b)
        {
            return;
        }

        adjacency[a].Add(b);
        adjacency[b].Add(a);
    }
}
