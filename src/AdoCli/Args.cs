namespace AdoCli;

using AdoCli.Api;

/// <summary>Positional arguments plus --options. Options not listed in <see cref="Flags"/> take a value; a repeated option keeps every value.</summary>
public sealed class Args
{
    static readonly HashSet<string> Flags = ["--debug", "--help", "-h", "--version", "--json", "--mine", "--yes", "-y", "--squash", "--insecure"];

    readonly Dictionary<string, List<string?>> _options = [];

    public List<string> Positional { get; } = [];

    public static Args Parse(IReadOnlyList<string> argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Count; i++)
        {
            var arg = argv[i];
            if (!arg.StartsWith('-') || arg == "-")
            {
                a.Positional.Add(arg);
                continue;
            }

            var eq = arg.IndexOf('=');
            if (eq > 0)
                a.Add(arg[..eq], arg[(eq + 1)..]);
            else if (Flags.Contains(arg))
                a.Add(arg, null);
            else if (i + 1 < argv.Count)
                a.Add(arg, argv[++i]);
            else
                throw AdoException.Usage($"option {arg} requires a value");
        }
        return a;
    }

    void Add(string name, string? value)
    {
        if (!_options.TryGetValue(name, out var values))
            _options[name] = values = [];
        values.Add(value);
    }

    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>The value of an option; the last one when it was given more than once.</summary>
    public string? Get(string name) => _options.GetValueOrDefault(name)?[^1];

    /// <summary>Every value of a repeatable option such as --field, in order.</summary>
    public IReadOnlyList<string?> GetAll(string name) => _options.GetValueOrDefault(name) ?? [];

    public string? At(int index) => index < Positional.Count ? Positional[index] : null;
}
