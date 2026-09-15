namespace AssetIndex.Discovery;

// A loaded package does not establish that its named export or subobject exists.
internal sealed class ReferenceClosure
{
    private readonly Dictionary<string, string> origins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> inventoryOnly = new(StringComparer.OrdinalIgnoreCase);

    public void Inventory(string target) => inventoryOnly.Add(target);

    public void Require(string source, string target)
    {
        // Package-only links (for example Outer) do not name an export.
        if (target.Contains('.')) origins.TryAdd(target, source);
    }

    public IEnumerable<ExtractionIssue> Check(IEnumerable<string> objects)
    {
        var found = objects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in origins)
            if (!found.Contains(target) && !inventoryOnly.Contains(target))
                yield return new("reference", source, $"Referenced object was not decoded: {target}.");
    }
}
