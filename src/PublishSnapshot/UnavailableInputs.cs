using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetIndex;
using CUE4Parse.UE4.IO.Objects;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed class UnavailableInputs
{
    private readonly PackageIndexRecord? index;
    private readonly HashSet<string> present = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> softPackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string, string)> edges = [];
    private readonly HashSet<string> usedImports = new(StringComparer.OrdinalIgnoreCase);
    public int Soft { get; private set; }
    public int Hard { get; private set; }

    public UnavailableInputs(IReadOnlyDictionary<string, SnapshotFile> files)
    {
        if (!files.ContainsKey("discovery/package-index.jsonl.gz")) return;
        PackageIndexRecord? captured = null;
        JsonLines.Read(files["discovery/package-index.jsonl.gz"], row =>
        {
            Require(captured is null, "Repeated package-index inventory.");
            UniqueFields(row);
            captured = JsonSerializer.Deserialize<PackageIndexRecord>(row, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Missing package-index evidence.");
        });
        index = captured ?? throw new InvalidDataException("Missing package-index evidence.");
        ValidateIndex(index);
        JsonLines.Read(files["discovery/packages.jsonl.gz"], row =>
        {
            var name = String(row, "name");
            Require(owners.TryAdd(name, String(row, "path")), "Duplicate package-index owner name.");
            present.Add(Hex(FPackageId.FromName(name).id));
        });
    }

    public bool Reference(JsonElement source, JsonElement reference)
    {
        if (!reference.TryGetProperty("unavailable", out var absence)) return false;
        Require(index is not null, "Unavailable reference lacks package-index evidence.");
        Fields(absence, "packageId", "ownerFile");
        var id = Id(absence, "packageId");
        Require(id != "ffffffffffffffff", "Invalid package ID cannot be declared unavailable.");
        Require(!present.Contains(id), "Unavailable reference has a present package identity.");
        var path = String(source, "path");
        var pointer = String(reference, "pointer");
        Require(edges.Add((path, pointer)), "Repeated unavailable reference edge.");
        Require(!reference.GetProperty("isNull").GetBoolean() && String(reference, "role") == "property" &&
            ReferencePolicy.RootPointer(pointer) is not null, "Unavailable reference is not an ordinary nonnull property.");
        Require(reference.GetProperty("package").ValueKind is JsonValueKind.String or JsonValueKind.Null,
            "Invalid reference package name.");
        Require(reference.GetProperty("exportIndex").ValueKind == JsonValueKind.Null, "Unavailable reference has a resolved export.");
        switch (String(reference, "kind"))
        {
            case "soft":
                Require(absence.GetProperty("ownerFile").ValueKind == JsonValueKind.Null &&
                    reference.GetProperty("error").ValueKind == JsonValueKind.Null &&
                    reference.GetProperty("packageIndex").ValueKind == JsonValueKind.Null, "Invalid unavailable soft reference.");
                var target = ResourceEvidence.ObjectPath(reference, "targetPath").Split('.', 2)[0];
                Require(!target.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) &&
                    Hex(FPackageId.FromName(target).id) == id, "Unavailable soft-reference identity differs from its path.");
                softPackages.Add(target);
                Soft++;
                break;
            case "hard":
                Require(reference.GetProperty("targetPath").ValueKind == JsonValueKind.Null &&
                    reference.GetProperty("error").ValueKind == JsonValueKind.String, "Invalid unavailable hard reference.");
                CheckImport(path, reference, String(absence, "ownerFile"), id);
                Hard++;
                break;
            default: throw new InvalidDataException("Unsupported unavailable-reference kind.");
        }
        return true;
    }

    public void Complete(MountedInputs inputs, JsonElement discovery)
    {
        CheckContainerCensus(discovery);
        foreach (var package in softPackages)
            Require(!owners.ContainsKey(package) && !inputs.RegistryOwners.ContainsKey(package) && !inputs.Paths.Contains(package),
                "Unavailable soft-reference package is mounted or registered.");
        Require(index is null || index.Imports.Select(row => row.File).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(usedImports),
            "Package import evidence has no unavailable reference.");
        Count("unavailableSoftReferences", Soft);
        Count("unavailableHardReferences", Hard);
        void Count(string field, int count)
        {
            if (discovery.TryGetProperty(field, out var value)) Require(value.GetInt32() == count, "Unavailable-reference coverage mismatch.");
            else Require(count == 0, "Unavailable references require explicit coverage counts.");
        }
    }

    private void CheckContainerCensus(JsonElement discovery)
    {
        if (!discovery.TryGetProperty("inputContainers", out var census) || census.ValueKind == JsonValueKind.Null)
        {
            Require(Soft + Hard == 0, "Unavailable references require an independent container census.");
            return;
        }
        Require(index is not null && census.ValueKind == JsonValueKind.Array, "Container census lacks its index evidence.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in census.EnumerateArray())
        {
            Fields(item, "path", "mounted", "normalPackages", "optionalPackages", "packageChunks", "redirects", "localizedPackages");
            var path = String(item, "path");
            Require(paths.Add(path), "Duplicate container census path.");
            var container = index!.Containers.SingleOrDefault(row => row.Path == path);
            Require(container is not null && container.Mounted == item.GetProperty("mounted").GetBoolean(), "Container census differs from inspected readers.");
            CheckCount(item, "normalPackages", container!.Normal.Count);
            CheckCount(item, "optionalPackages", container.Optional.Count);
            CheckCount(item, "packageChunks", container.PackageChunks.Count);
            CheckCount(item, "redirects", container.Redirects.Count);
            CheckCount(item, "localizedPackages", container.Localized.Count);
        }
        Require(paths.SetEquals(index!.Containers.Select(row => row.Path)), "Container census and index inventory differ.");
    }

    private void CheckImport(string source, JsonElement reference, string file, string target)
    {
        var owner = index!.Imports.SingleOrDefault(row => row.File.Equals(file, StringComparison.OrdinalIgnoreCase));
        Require(owner is not null && owners.GetValueOrDefault(owner.Name)?.Equals(file, StringComparison.OrdinalIgnoreCase) == true &&
            source.Split('.', 2)[0].Equals(owner.Name, StringComparison.OrdinalIgnoreCase) &&
            String(reference, "package").Equals(owner.Name, StringComparison.OrdinalIgnoreCase), "Unavailable import has the wrong owning package.");
        var mapSlot = -(long)reference.GetProperty("packageIndex").GetInt32() - 1;
        Require(mapSlot >= 0 && mapSlot < owner!.ImportMap.Count, "Unavailable import index is outside the owner import map.");
        var entry = new FPackageObjectIndex(ParseId(owner!.ImportMap[(int)mapSlot]));
        Require(entry.IsPackageImport, "Unavailable import is not a package import.");
        var imported = entry.AsPackageImportRef;
        Require(imported.ImportedPublicExportHashIndex < owner.ExportHashes.Count, "Unavailable import export hash index is invalid.");
        var container = index.Containers.Single(row => row.Path == owner.Container);
        var stores = container.Normal.Concat(container.Optional).Where(row => row.Id == owner.Id).ToArray();
        Require(stores.Length == 1 && imported.ImportedPackageIndex < stores[0].Imports.Count &&
            stores[0].Imports[(int)imported.ImportedPackageIndex] == target, "Unavailable import differs from its owning store binding.");
        Require(index.Effective.Any(row => row.Id == owner.Id && row.Path == owner.File && row.Container == owner.Container),
            "Unavailable import owner is not the effective physical package.");
        usedImports.Add(file);
    }

    private void ValidateIndex(PackageIndexRecord value)
    {
        Require(value.Containers is not null && value.Effective is not null && value.Imports is not null, "Incomplete package-index evidence.");
        var containers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in value.Containers!)
        {
            Require(!string.IsNullOrWhiteSpace(container.Path) && containers.Add(container.Path), "Duplicate or invalid package container.");
            ValidateContainer(container);
        }
        var effective = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in value.Effective!)
        {
            Require(effective.Add(row.Id) && containers.Contains(row.Container) && !string.IsNullOrWhiteSpace(row.Path), "Invalid effective package binding.");
            AddIds([row.Id]);
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in value.Imports!)
        {
            Require(files.Add(row.File) && containers.Contains(row.Container) && row.ImportMap is not null && row.ExportHashes is not null &&
                row.Name.StartsWith('/') && Hex(FPackageId.FromName(row.Name).id) == row.Id, "Invalid package import owner.");
            foreach (var id in row.ImportMap!.Concat(row.ExportHashes!)) ParseId(id);
        }
    }

    private void ValidateContainer(ContainerPackages container)
    {
        Require(container.PackageChunks is not null && container.Normal is not null && container.Optional is not null &&
            container.Redirects is not null && container.Localized is not null, "Incomplete package container.");
        Require(container.Mounted || !container.HasHeader && container.PackageChunks!.Count == 0,
            "Uninspected package-bearing container.");
        Require(container.HasHeader || container.PackageChunks!.Count == 0, "Package-bearing container has no header.");
        Require(container.HasHeader || container.Normal!.Count + container.Optional!.Count + container.Redirects!.Count + container.Localized!.Count == 0,
            "Package store evidence has no container header.");
        AddIds(container.PackageChunks!);
        foreach (var stores in new[] { container.Normal!, container.Optional! })
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var store in stores)
            {
                Require(ids.Add(store.Id) && store.Imports is not null, "Invalid or duplicate package store entry.");
                AddIds([store.Id]);
                foreach (var imported in store.Imports!) ParseId(imported);
            }
        }
        AddIds(container.Redirects!.SelectMany(row => new[] { row.Source, row.Target }));
        AddIds(container.Localized!);
    }

    private void AddIds(IEnumerable<string> ids)
    {
        foreach (var id in ids) { ParseId(id); present.Add(id); }
    }
    private static string Id(JsonElement value, string field) { var id = String(value, field); ParseId(id); return id; }
    private static ulong ParseId(string text)
    {
        Require(text is not null && text.Length == 16 && text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "Package identity must be canonical hexadecimal.");
        return ulong.Parse(text!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

    private static void UniqueFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) UniqueFields(item);
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            Require(names.Add(field.Name), "Duplicate package-index field.");
            UniqueFields(field.Value);
        }
    }

    public static void ValidateDependencies(SnapshotFile objects, IdentitySchemas schemas)
    {
        JsonLines.Read(objects, source =>
        {
            foreach (var reference in source.GetProperty("references").EnumerateArray())
            {
                if (!reference.TryGetProperty("unavailable", out _)) continue;
                var pointer = String(reference, "pointer");
                var root = ReferencePolicy.RootPointer(pointer);
                var fields = source.GetProperty("properties").EnumerateArray().Where(row => String(row, "pointer") == root).ToArray();
                Require(fields.Length == 1, "Unavailable reference has no unique root property.");
                var schema = schemas.Schema(String(source, "path"));
                var name = String(fields[0], "name");
                Require(ReferencePolicy.AllowsUnavailable(schema.NativeAncestry, name) &&
                    schema.Property(name, String(fields[0], "type")) is not null, $"Catalog dependency cannot be unavailable: {name}.");
            }
        });
    }
}
