using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Export headers account for every sibling without requiring every actor or mesh
// payload to be decoded. Selected bodies must still have complete object evidence.
internal sealed class ExportCoverage
{
    private static readonly string[] Roots = ["DataAsset", "UIMetaDataItem", "DataTable", "CurveTable", "StringTable", "Blueprint", "BlueprintGeneratedClass"];
    private sealed record Header(string Path, string Class, string? SuperPath, string[] Ancestry)
    {
        public bool Candidate => Roots.Any(root => Ancestry.Contains(root, StringComparer.OrdinalIgnoreCase));
        public bool Follow => Candidate || Ancestry.Contains("Struct", StringComparer.OrdinalIgnoreCase) ||
            Ancestry.Contains("Texture", StringComparer.OrdinalIgnoreCase) ||
            Ancestry.Contains("MaterialInterface", StringComparer.OrdinalIgnoreCase);
    }
    private sealed record Package(string Name, int Exports, HashSet<int> Selected);

    private readonly Dictionary<string, Package> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Header> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortedDictionary<int, Header>> headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> decoded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public static void Validate(IReadOnlyDictionary<string, SnapshotFile> files, JsonElement report, MountedInputs inputs,
        IReadOnlyDictionary<string, string> objects, IEnumerable<string> targets, IReadOnlySet<string> uiTextures)
    {
        var coverage = new ExportCoverage();
        coverage.ReadPackages(files["discovery/packages.jsonl.gz"], inputs);
        CheckCount(report, "candidates", coverage.packages.Count);
        Require(inputs.Paths.SetEquals(coverage.packages.Keys), "A mounted package was not inspected.");
        coverage.ReadHeaders(files["discovery/exports.jsonl.gz"]);
        coverage.CheckObjects(objects, uiTextures);
        coverage.CheckReferences(targets.Concat(coverage.paths.Values.Select(header => header.SuperPath).OfType<string>()), inputs);
    }

    private void ReadPackages(SnapshotFile file, MountedInputs inputs) => JsonLines.Read(file, row =>
    {
        Fields(row, "path", "name", "reason", "status", "exports", "selected", "decoded");
        var path = String(row, "path");
        Require(inputs.Paths.Contains(path), $"Discovered package is absent from mounted inputs: {path}.");
        var name = ResourceEvidence.ObjectPath(row, "name");
        Require(!name.Contains('.') && names.Add(name), "Invalid or duplicate inspected package name.");
        Require(!inputs.RegistryOwners.TryGetValue(name, out var canonicalOwner) || canonicalOwner.Equals(path, StringComparison.OrdinalIgnoreCase),
            "Inspected package name contradicts its mounted registry owner.");
        String(row, "reason");
        var exports = row.GetProperty("exports").GetInt32();
        Require(String(row, "status") == "succeeded" && exports >= 0, "Discovery contains an incomplete package.");
        var selected = Indices(row.GetProperty("selected"), exports);
        Require(selected.SetEquals(Indices(row.GetProperty("decoded"), exports)), "A selected export was not decoded.");
        Require(packages.TryAdd(path, new(name, exports, selected)), "Duplicate discovered package.");
        headers.Add(path, []);
    });

    private void ReadHeaders(SnapshotFile file) => JsonLines.Read(file, row =>
    {
        Fields(row, "package", "index", "path", "class", "classPath", "superPath", "ancestry", "ancestryComplete", "error");
        var package = String(row, "package");
        Require(packages.TryGetValue(package, out var owner), "Export header lacks an inspected package.");
        var index = row.GetProperty("index").GetInt32();
        Require(index >= 0 && index < owner!.Exports, "Export header index is outside its package.");
        Require(row.GetProperty("error").ValueKind == JsonValueKind.Null && row.GetProperty("ancestryComplete").GetBoolean(),
            "Export header metadata is incomplete.");
        var path = ResourceEvidence.ObjectPath(row, "path");
        Require(path.Contains('.') && PackageName(path).Equals(owner!.Name, StringComparison.OrdinalIgnoreCase), "Export header has the wrong package name.");
        var type = String(row, "class");
        ResourceEvidence.ObjectPath(row, "classPath");
        var superPath = row.GetProperty("superPath").ValueKind == JsonValueKind.Null
            ? null : ResourceEvidence.ObjectPath(row, "superPath");
        Require(superPath is null || superPath.Contains('.'), "Superclass header must name an object.");
        var ancestry = row.GetProperty("ancestry").EnumerateArray().Select(value =>
        {
            Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()), "Invalid export ancestry.");
            return value.GetString()!;
        }).ToArray();
        Require(ancestry.Length is > 0 and <= 128 && ancestry[0].Equals(type, StringComparison.OrdinalIgnoreCase) &&
            ancestry[^1].Equals("Object", StringComparison.OrdinalIgnoreCase), "Incomplete export ancestry.");
        var header = new Header(path, type, superPath, ancestry);
        Require(headers[package].TryAdd(index, header), "Duplicate export header index.");
        Require(paths.TryAdd(path, header), "Ambiguous export header paths differ only by case or repeat.");
    });

    private void CheckObjects(IReadOnlyDictionary<string, string> objects, IReadOnlySet<string> uiTextures)
    {
        foreach (var (package, owner) in packages)
        {
            var exports = headers[package];
            Require(exports.Count == owner.Exports, "Export header inventory is incomplete.");
            foreach (var (index, header) in exports)
            {
                var selected = owner.Selected.Contains(index);
                Require(selected || !(header.Candidate || uiTextures.Contains(header.Path)), $"Required export was not selected: {header.Path}.");
                if (!selected) continue;
                Require(objects.TryGetValue(header.Path, out var type) && type!.Equals(header.Class, StringComparison.OrdinalIgnoreCase), $"Selected export lacks matching object evidence: {header.Path}.");
                decoded.Add(header.Path);
            }
        }
        Require(decoded.SetEquals(objects.Keys), "Object evidence includes an unselected or uninventoried export.");
        Require(uiTextures.IsSubsetOf(decoded), "A registry UI texture was not decoded.");
    }

    private void CheckReferences(IEnumerable<string> targets, MountedInputs inputs)
    {
        foreach (var target in targets)
        {
            if (target.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)) continue;
            var name = PackageName(target);
            Require(names.Contains(name) || inputs.RegistryOwners.TryGetValue(name, out var physical) && packages.ContainsKey(physical),
                $"Referenced package was not inspected: {target}.");
            // Package aliases establish a read, never an invented object/outer path.
            if (!target.Contains('.')) continue;
            Require(paths.TryGetValue(target, out var header), $"Named reference target is absent from an inspected package: {target}.");
            Require(!header!.Follow || decoded.Contains(target), $"Referenced export was not decoded: {target}.");
        }
    }

    private static HashSet<int> Indices(JsonElement value, int exports)
    {
        var result = new HashSet<int>();
        var previous = -1;
        foreach (var item in value.EnumerateArray())
        {
            var index = item.GetInt32();
            Require(index > previous && index < exports, "Export indices must be sorted, unique, and inside the package.");
            result.Add(index);
            previous = index;
        }
        return result;
    }

    private static string PackageName(string path) => path.Split('.', 2)[0];
}
