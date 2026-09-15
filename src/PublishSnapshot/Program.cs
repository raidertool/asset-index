namespace PublishSnapshot;

internal static class Program
{
    internal const string Usage = "PublishSnapshot <preview-directory> <remote> <extractor-commit> <manifest-id>";

    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }
        if (args.Length != 4)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        try
        {
            var result = Publisher.Publish(args[0], args[1], args[2], args[3]);
            Console.WriteLine($"{(result.Changed ? "Published" : "Unchanged")}: {result.Commit} {result.Tag}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
