using AssetIndex;
using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Export headers account for every sibling without requiring every actor or mesh
// payload to be decoded. Selected bodies must still have complete object evidence.
internal sealed class ExportCoverage
{
    private static readonly string[] Roots = ["DataAsset", "UIMetaDataItem", "DataTable", "CurveTable", "StringTable", "Blueprint", "BlueprintGeneratedClass"];
    private static readonly string[] ReferenceRoots = ["Struct", "Texture", "MaterialInterface", "Widget", "WidgetTree", "PanelSlot"];
    private sealed record Header(string Path, string Class, string ClassPath, string? SuperPath,
        string[] Ancestry, bool Complete)
    {
        public bool Candidate => Roots.Any(root => Ancestry.Contains(root, StringComparer.OrdinalIgnoreCase));
        public bool Follow => Candidate || ReferenceRoots.Any(root => Ancestry.Contains(root, StringComparer.OrdinalIgnoreCase));
    }
    private sealed record Package(string Name, int Exports, HashSet<int> Selected);

    private readonly Dictionary<string, Package> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Header> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortedDictionary<int, Header>> headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> decoded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> headerOnly = new(StringComparer.OrdinalIgnoreCase);

    public static void Validate(IReadOnlyDictionary<string, SnapshotFile> files, JsonElement report, MountedInputs inputs,
        IReadOnlyDictionary<string, string> objects, IEnumerable<string> targets, IReadOnlySet<string> uiTextures,
        IReadOnlySet<string> requiredBodies)
    {
        var coverage = new ExportCoverage();
        coverage.ReadPackages(files["discovery/packages.jsonl.gz"], inputs);
        CheckCount(report, "candidates", coverage.packages.Count);
        Require(inputs.Paths.SetEquals(coverage.packages.Keys), "A mounted package was not inspected.");
        coverage.ReadHeaders(files["discovery/exports.jsonl.gz"]);
        coverage.CheckScopes(files, report);
        coverage.CheckObjects(objects, uiTextures);
        coverage.CheckReferences(targets.Concat(coverage.paths.Values.Select(header => header.SuperPath).OfType<string>()), inputs, requiredBodies);
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
        var complete = row.GetProperty("ancestryComplete").GetBoolean();
        Require(complete == (row.GetProperty("error").ValueKind == JsonValueKind.Null), "Export header metadata is inconsistent.");
        if (!complete) String(row, "error");
        var path = ResourceEvidence.ObjectPath(row, "path");
        Require(path.Contains('.') && PackageName(path).Equals(owner!.Name, StringComparison.OrdinalIgnoreCase), "Export header has the wrong package name.");
        var type = String(row, "class");
        var classPath = ResourceEvidence.ObjectPath(row, "classPath");
        Require(classPath.Contains('.') && ShortName(classPath).Equals(type, StringComparison.OrdinalIgnoreCase),
            "Export class differs from its qualified class path.");
        var superPath = row.GetProperty("superPath").ValueKind == JsonValueKind.Null
            ? null : ResourceEvidence.ObjectPath(row, "superPath");
        Require(superPath is null || superPath.Contains('.'), "Superclass header must name an object.");
        var ancestry = row.GetProperty("ancestry").EnumerateArray().Select(value =>
        {
            Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()), "Invalid export ancestry.");
            return value.GetString()!;
        }).ToArray();
        Require(ancestry.Length is > 0 and <= 128 && ancestry[0].Equals(type, StringComparison.OrdinalIgnoreCase) &&
            (!complete || ancestry[^1].Equals("Object", StringComparison.OrdinalIgnoreCase)), "Incomplete export ancestry.");
        var header = new Header(path, type, classPath, superPath, ancestry, complete);
        Require(headers[package].TryAdd(index, header), "Duplicate export header index.");
        Require(paths.TryAdd(path, header), "Ambiguous export header paths differ only by case or repeat.");
    });

    private void CheckScopes(IReadOnlyDictionary<string, SnapshotFile> files, JsonElement report)
    {
        var discovery = report.GetProperty("discovery");
        var incomplete = paths.Values.Where(header => !header.Complete).ToArray();
        if (incomplete.Length > 0)
        {
            var hash = String(discovery, "mappingSha256");
            var mappings = IdentitySchemas.LoadMappings(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"), hash);
            var scope = new ClassScope(mappings, hash);
            var declarations = ReadScopeDeclarations(files["discovery/objects.jsonl.gz"]);
            foreach (var header in incomplete)
            {
                Require(scope.CanOmitBody(header.ClassPath, path => declarations.GetValueOrDefault(path)),
                    $"Export header metadata is incomplete: {header.Path}.");
                // The recorded partial ancestry must be the actual runtime chain
                // ending at the missing native layout, not an invented root.
                var ancestry = new List<string>();
                var current = header.ClassPath;
                while (!current.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase))
                {
                    ancestry.Add(ShortName(current));
                    current = paths[current].SuperPath!;
                }
                ancestry.Add(ShortName(current));
                Require(header.Ancestry.SequenceEqual(ancestry, StringComparer.OrdinalIgnoreCase),
                    "Incomplete ancestry contradicts the verified class declarations.");
                headerOnly.Add(header.Path);
            }
        }
        var declared = discovery.TryGetProperty("unmappedNonCatalogExports", out var value) ? value.GetInt32() : 0;
        Require(declared == headerOnly.Count, "Unmapped non-catalog export count is inconsistent.");
    }

    private Dictionary<string, ClassScopeDeclaration> ReadScopeDeclarations(SnapshotFile file)
    {
        var result = new Dictionary<string, ClassScopeDeclaration>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(file, row =>
        {
            var path = String(row, "path");
            if (!paths.TryGetValue(path, out var header) || !header.Complete || header.SuperPath is null) return;
            if (!header.ClassPath.Equals("/Script/UMG.WidgetBlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) &&
                !header.ClassPath.Equals("/Script/Engine.BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase)) return;
            var references = row.GetProperty("references").EnumerateArray().ToArray();
            if (!Matches("/Class", "class", "resolved", header.ClassPath) ||
                !Matches("/Native/SuperStruct", "super", "hard", header.SuperPath)) return;
            result.Add(path, new(header.ClassPath, header.SuperPath, true));
            bool Matches(string pointer, string role, string kind, string target)
            {
                var links = references.Where(edge => String(edge, "pointer") == pointer).ToArray();
                return links.Length == 1 && String(links[0], "role") == role && String(links[0], "kind") == kind &&
                    links[0].GetProperty("targetPath").ValueKind == JsonValueKind.String &&
                    String(links[0], "targetPath").Equals(target, StringComparison.OrdinalIgnoreCase) &&
                    !row.GetProperty("values").EnumerateArray().Concat(row.GetProperty("texts").EnumerateArray())
                        .Any(value => String(value, "pointer") == pointer ||
                            String(value, "pointer").StartsWith(pointer + "/", StringComparison.Ordinal));
            }
        });
        return result;
    }

    private void CheckObjects(IReadOnlyDictionary<string, string> objects, IReadOnlySet<string> uiTextures)
    {
        foreach (var (package, owner) in packages)
        {
            var exports = headers[package];
            Require(exports.Count == owner.Exports, "Export header inventory is incomplete.");
            foreach (var (index, header) in exports)
            {
                var selected = owner.Selected.Contains(index);
                Require(!headerOnly.Contains(header.Path) || !selected, "An export with an unknown field layout was decoded.");
                Require(selected || !(header.Candidate || uiTextures.Contains(header.Path)), $"Required export was not selected: {header.Path}.");
                if (!selected) continue;
                Require(objects.TryGetValue(header.Path, out var type) && type!.Equals(header.Class, StringComparison.OrdinalIgnoreCase), $"Selected export lacks matching object evidence: {header.Path}.");
                decoded.Add(header.Path);
            }
        }
        Require(decoded.SetEquals(objects.Keys), "Object evidence includes an unselected or uninventoried export.");
        Require(uiTextures.IsSubsetOf(decoded), "A registry UI texture was not decoded.");
    }

    private void CheckReferences(IEnumerable<string> targets, MountedInputs inputs, IReadOnlySet<string> requiredBodies)
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
            if (!headerOnly.Contains(target) && (header!.Follow || requiredBodies.Contains(target)))
                Require(decoded.Contains(target), $"Referenced export was not decoded: {target}.");
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
    private static string ShortName(string path) => path[(path.LastIndexOf('.') + 1)..];
}
