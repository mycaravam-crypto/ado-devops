namespace AdoCli;

using AdoCli.Api;

/// <summary>Positional arguments plus --options. Options not listed in <see cref="Flags"/> take a value.</summary>
public sealed class Args
{
    static readonly HashSet<string> Flags = ["--debug", "--help", "-h", "--version"];

    readonly Dictionary<string, string?> _options = [];

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
                a._options[arg[..eq]] = arg[(eq + 1)..];
            else if (Flags.Contains(arg))
                a._options[arg] = null;
            else if (i + 1 < argv.Count)
                a._options[arg] = argv[++i];
            else
                throw AdoException.Usage($"option {arg} requires a value");
        }
        return a;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public string? At(int index) => index < Positional.Count ? Positional[index] : null;
}
