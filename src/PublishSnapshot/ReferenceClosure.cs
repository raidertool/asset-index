namespace PublishSnapshot;

internal static class ReferenceClosure
{
    public static void Validate(IReadOnlySet<string> objects, IEnumerable<string> targets)
    {
        var paths = objects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Preview.Require(paths.Count == objects.Count, "Ambiguous discovered object paths differ only by case.");
        var packages = paths.Select(Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            // This proves closure only within packages represented by object evidence.
            // Native, package-only, and unobserved-package references need other evidence.
            if (target.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) || !target.Contains('.')) continue;
            Preview.Require(!packages.Contains(Package(target)) || paths.Contains(target),
                $"Named reference target is absent from a discovered package: {target}.");
        }
    }

    private static string Package(string path) => path.Split('.', 2)[0];
}
