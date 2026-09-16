namespace PublishSnapshot;

internal static class Program
{
    internal const string Usage = "PublishSnapshot [--init] <preview-directory> <remote> <extractor-commit> <manifest-id>";

    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }
        var initialize = args is ["--init", ..];
        var values = initialize ? args[1..] : args;
        if (values.Length != 4)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        try
        {
            var result = initialize
                ? Publisher.Initialize(values[0], values[1], values[2], values[3])
                : Publisher.Publish(values[0], values[1], values[2], values[3]);
            var action = initialize ? "Initialized" : result.Changed ? "Published" : "Unchanged";
            Console.WriteLine($"{action}: {result.Commit} {result.Tag}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
