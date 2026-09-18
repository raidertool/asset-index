namespace PublishSnapshot;

internal static class Program
{
    internal const string Usage = """
        PublishSnapshot <preview-directory> <remote> <extractor-commit> <manifest-id>
        PublishSnapshot --export <preview-directory> <new-output-directory> <extractor-commit> <manifest-id>
        PublishSnapshot --export-digest <export-directory>
        PublishSnapshot --publish-export <export-directory> <remote> <extractor-commit> <manifest-id> <export-sha256> <expected-metadata-blob>
        """;

    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }
        var digest = args is ["--export-digest", ..];
        var export = args is ["--export", ..];
        var publishExport = args is ["--publish-export", ..];
        var values = digest || export || publishExport ? args[1..] : args;
        if (values.Length != (digest ? 1 : publishExport ? 6 : 4))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        try
        {
            if (digest)
            {
                using var exported = PublicExport.Read(values[0]);
                Console.WriteLine(exported.ExportSha256);
            }
            else if (export)
            {
                var metadata = Publisher.Export(values[0], values[1], values[2], values[3]);
                Console.WriteLine($"Exported: {metadata.ContentSha256} {Publisher.Tag(metadata.Steam.ManifestId, metadata.ContentSha256)}");
            }
            else
            {
                var result = publishExport
                    ? Publisher.PublishExport(values[0], values[1], values[2], values[3], values[4], values[5])
                    : Publisher.Publish(values[0], values[1], values[2], values[3]);
                Console.WriteLine($"{(result.Changed ? "Published" : "Unchanged")}: {result.Commit} {result.Tag}");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
