using System.Globalization;
using GossNet.Observatory.Cluster;

namespace GossNet.Observatory;

/// <summary>
/// A small <c>--key value</c> parser.
/// </summary>
/// <remarks>
/// Hand-rolled on purpose. The app has a handful of options, and Spectre.Console.Cli
/// would add a dependency whose stable release line is not obvious from its feed.
/// </remarks>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine()
    {
    }

    /// <summary>Parses the raw argument list.</summary>
    public static CommandLine Parse(string[] args)
    {
        var command = new CommandLine();

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argument[2..];

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                command._values[name] = args[++i];
            }
            else
            {
                command._flags.Add(name);
            }
        }

        return command;
    }

    /// <summary>Determines whether a valueless switch was given.</summary>
    public bool HasFlag(string name) => _flags.Contains(name);

    /// <summary>Reads an integer option.</summary>
    public int Int(string name, int fallback) =>
        _values.TryGetValue(name, out var raw) && int.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Reads a floating-point option.</summary>
    public double Double(string name, double fallback) =>
        _values.TryGetValue(name, out var raw) && double.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Reads a string option.</summary>
    public string? String(string name) => _values.GetValueOrDefault(name);

    /// <summary>Reads a comma-separated list of integers.</summary>
    public IReadOnlyList<int> Ints(string name, IReadOnlyList<int> fallback) =>
        Split(name, part => int.TryParse(part, CultureInfo.InvariantCulture, out var value) ? value : (int?)null) ?? fallback;

    /// <summary>Reads a comma-separated list of fractions.</summary>
    public IReadOnlyList<double> Doubles(string name, IReadOnlyList<double> fallback) =>
        Split(name, part => double.TryParse(part, CultureInfo.InvariantCulture, out var value) ? value : (double?)null) ?? fallback;

    /// <summary>Reads a comma-separated list of topologies.</summary>
    public IReadOnlyList<TopologyKind> Topologies(string name, IReadOnlyList<TopologyKind> fallback) =>
        Split(name, part => Enum.TryParse<TopologyKind>(part, ignoreCase: true, out var value) ? value : (TopologyKind?)null)
        ?? fallback;

    private List<T>? Split<T>(string name, Func<string, T?> convert) where T : struct
    {
        if (!_values.TryGetValue(name, out var raw))
        {
            return null;
        }

        var parsed = new List<T>();

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (convert(part) is { } value)
            {
                parsed.Add(value);
            }
            else
            {
                throw new ArgumentException($"Could not read '{part}' in --{name}.");
            }
        }

        return parsed.Count == 0 ? null : parsed;
    }
}
