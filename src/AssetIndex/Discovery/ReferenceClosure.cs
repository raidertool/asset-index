namespace AssetIndex.Discovery;

// A loaded package does not establish that its named export or subobject exists.
internal sealed class ReferenceClosure
{
    private readonly Dictionary<string, string> origins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> inventoryOnly = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> requiredBodies = new(StringComparer.OrdinalIgnoreCase);

    public void Inventory(string target) => inventoryOnly.Add(target);

    public bool NeedsBody(string target) => requiredBodies.Contains(target);

    // A later class-default or template reference can upgrade an inventoried target.
    public bool Require(string source, string target, bool requireBody = false)
    {
        // Package-only links (for example Outer) do not name an export.
        if (!target.Contains('.')) return false;
        var added = origins.TryAdd(target, source);
        var promoted = requireBody && requiredBodies.Add(target);
        return added || promoted;
    }

    public IEnumerable<ExtractionIssue> Check(IEnumerable<string> objects, IReadOnlySet<string>? provenHeaderOnly = null)
    {
        var found = objects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in origins)
            if (!found.Contains(target) && !(provenHeaderOnly?.Contains(target) ?? false) &&
                (NeedsBody(target) || !inventoryOnly.Contains(target)))
                yield return new("reference", source, $"Referenced object was not decoded: {target}.");
    }
}
