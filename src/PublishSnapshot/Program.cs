namespace PublishSnapshot;

internal static class Program
{
    internal const string Usage = "PublishSnapshot <preview-directory> <remote> <extractor-commit> <manifest-id>\nPublishSnapshot --export <preview-directory> <new-output-directory> <extractor-commit> <manifest-id>";

    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }
        var export = args is ["--export", ..];
        var values = export ? args[1..] : args;
        if (values.Length != 4)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        try
        {
            if (export)
            {
                var metadata = Publisher.Export(values[0], values[1], values[2], values[3]);
                Console.WriteLine($"Exported: {metadata.ContentSha256} {Publisher.Tag(metadata.Steam.ManifestId, metadata.ContentSha256)}");
            }
            else
            {
                var result = Publisher.Publish(values[0], values[1], values[2], values[3]);
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
