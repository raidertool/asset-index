namespace AssetIndex;

internal sealed record Options(string GameDirectory, string Usmap, string OutputDirectory)
{
    public const string Usage = "AssetIndex --game-dir <game files> --output <empty directory> [--usmap <file>]";

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            var name = args[i];
            if (name is not ("--game-dir" or "--usmap" or "--output"))
                throw new ArgumentException($"Unknown option: {name}");
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {name}");
            if (!values.TryAdd(name, args[i + 1]))
                throw new ArgumentException($"Repeated option: {name}");
        }
        if (!values.TryGetValue("--game-dir", out var gameDirectory) || !values.TryGetValue("--output", out var output))
            throw new ArgumentException(Usage);
        var usmap = values.GetValueOrDefault("--usmap", Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
        return new Options(Path.GetFullPath(gameDirectory), Path.GetFullPath(usmap), Path.GetFullPath(output));
    }

    public void Validate()
    {
        if (!Directory.Exists(GameDirectory))
            throw new DirectoryNotFoundException($"Game directory does not exist: {GameDirectory}");
        if (!File.Exists(Usmap))
            throw new FileNotFoundException("Usmap does not exist.", Usmap);
        if (File.Exists(OutputDirectory) || Directory.Exists(OutputDirectory) && Directory.EnumerateFileSystemEntries(OutputDirectory).Any())
            throw new IOException("Output must be a new or empty directory; existing snapshots are never overwritten.");
    }
}
