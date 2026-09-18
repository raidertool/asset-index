using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal sealed class PackageInputIndex
{
    private readonly PackageProvider provider;
    private readonly ContainerPackages[] containers;
    private readonly IndexedPackage[] effective;
    private readonly HashSet<string> available;
    private readonly Dictionary<string, PackageImports> imports = new(StringComparer.OrdinalIgnoreCase);

    public PackageInputIndex(PackageProvider provider)
    {
        this.provider = provider;
        var mounted = provider.MountedVfs.ToHashSet();
        containers = provider.MountedVfs.Concat(provider.UnloadedVfs).OfType<IoStoreReader>()
            .OrderBy(reader => reader.Path, StringComparer.Ordinal)
            .Select(reader => ReadContainer(reader, mounted.Contains(reader))).ToArray();
        effective = provider.FilesById.OrderBy(pair => pair.Key.id).Select(pair => new IndexedPackage(
            Id(pair.Key.id), pair.Value.Path, (pair.Value as FIoStoreEntry)?.IoStoreReader.Path
                ?? throw new InvalidDataException("Indexed package has no IoStore owner."))).ToArray();
        available = containers.SelectMany(container => container.PackageChunks
            .Concat(container.Normal.Select(row => row.Id)).Concat(container.Optional.Select(row => row.Id))
            .Concat(container.Redirects.SelectMany(row => new[] { row.Source, row.Target })).Concat(container.Localized))
            .Concat(effective.Select(row => row.Id)).ToHashSet(StringComparer.Ordinal);
    }

    public PackageIndexRecord Record => new(containers, effective, imports.Values.OrderBy(row => row.File, StringComparer.Ordinal).ToArray());

    public static IReadOnlyList<InputContainer> Census(PackageProvider provider)
    {
        var mounted = provider.MountedVfs.ToHashSet();
        return provider.MountedVfs.Concat(provider.UnloadedVfs).OfType<IoStoreReader>()
            .OrderBy(reader => reader.Path, StringComparer.Ordinal).Select(reader =>
            {
                var hasHeader = reader.TocResource.ChunkIds.Any(chunk => chunk.ChunkType == (byte)EIoChunkType5.ContainerHeader);
                var header = hasHeader ? reader.ContainerHeader : null;
                return new InputContainer(reader.Path, mounted.Contains(reader), header?.PackageIds.Length ?? 0,
                    header?.OptionalSegmentPackageIds?.Length ?? 0,
                    reader.TocResource.ChunkIds.Where(chunk => chunk.ChunkType is
                        (byte)EIoChunkType5.ExportBundleData or (byte)EIoChunkType5.PackageStoreEntry)
                        .Select(chunk => chunk.ChunkId).Distinct().Count(),
                    header?.PackageRedirects.Length ?? 0, header?.LocalizedPackages?.Length ?? 0);
            }).ToArray();
    }

    public UnavailableReference? MissingSoft(string target)
    {
        var package = target.Split('.', 2)[0];
        if (!package.StartsWith('/') || package.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) ||
            provider.TryGetGameFile(GameFiles.ResolvePackagePath(provider, package), out _)) return null;
        var packageId = FPackageId.FromName(package).id;
        if (provider.HasLoadedPackageId(packageId)) return null;
        var id = Id(packageId);
        return available.Contains(id) ? null : new(id, null);
    }

    public UnavailableReference? MissingHard(FPackageIndex reference)
    {
        if (reference.Owner is not IoPackage package || !reference.IsImport ||
            package.ImportedPublicExportHashes is not { } hashes ||
            provider.SourceFile(package) is not FIoStoreEntry file) return null;
        var slot = -(long)reference.Index - 1;
        if (slot < 0 || slot >= package.ImportMap.Length) return null;
        var index = package.ImportMap[(int)slot];
        if (!index.IsPackageImport) return null;
        var imported = index.AsPackageImportRef;
        if (imported.ImportedPublicExportHashIndex >= hashes.Length) return null;
        var ownerId = Id(FPackageId.FromName(package.Name).id);
        var owner = containers.Single(row => row.Path == file.IoStoreReader.Path);
        var stores = owner.Normal.Concat(owner.Optional).Where(row => row.Id == ownerId).ToArray();
        if (stores.Length != 1 || imported.ImportedPackageIndex >= stores[0].Imports.Count) return null;
        var target = stores[0].Imports[(int)imported.ImportedPackageIndex];
        if (target == "ffffffffffffffff") return null; // Unreal's invalid package ID is not an absent package.
        if (available.Contains(target) || provider.HasLoadedPackageId(ulong.Parse(target, System.Globalization.NumberStyles.HexNumber))) return null;
        imports.TryAdd(file.Path, new(file.Path, package.Name, owner.Path, ownerId,
            package.ImportMap.Select(value => Id(value.TypeAndId)).ToArray(), hashes.Select(Id).ToArray()));
        return new(target, file.Path);
    }

    private static ContainerPackages ReadContainer(IoStoreReader reader, bool mounted)
    {
        var chunks = reader.TocResource.ChunkIds.Where(chunk => chunk.ChunkType is
            (byte)EIoChunkType5.ExportBundleData or (byte)EIoChunkType5.PackageStoreEntry)
            .Select(chunk => Id(chunk.ChunkId)).Distinct().Order(StringComparer.Ordinal).ToArray();
        var hasHeader = reader.TocResource.ChunkIds.Any(chunk => chunk.ChunkType == (byte)EIoChunkType5.ContainerHeader);
        if (!mounted && (hasHeader || chunks.Length > 0))
            throw new InvalidDataException("Package container metadata is unavailable or not mounted.");
        if (!hasHeader)
        {
            if (chunks.Length > 0) throw new InvalidDataException("Package-bearing container has no header.");
            return new(reader.Path, mounted, false, [], [], [], [], []);
        }
        var header = reader.ContainerHeader ?? throw new InvalidDataException("Container header is unavailable.");
        return new(reader.Path, mounted, hasHeader, chunks,
            Stores(header.PackageIds, header.StoreEntries), Stores(header.OptionalSegmentPackageIds, header.OptionalSegmentStoreEntries),
            header.PackageRedirects.Select(row => new PackageRedirect(Id(row.SourcePackageId.id), Id(row.TargetPackageId.id))).ToArray(),
            (header.LocalizedPackages ?? []).Select(row => Id(row.SourcePackageId.id)).ToArray());
    }

    private static StoredPackage[] Stores(FPackageId[]? ids, FFilePackageStoreEntry[]? entries)
    {
        if (ids is null && entries is null) return [];
        if (ids is null || entries is null || ids.Length != entries.Length)
            throw new InvalidDataException("Package store IDs and entries differ.");
        return ids.Select((id, index) => new StoredPackage(Id(id.id),
            (entries[index].ImportedPackages ?? throw new InvalidDataException("Package imports unavailable."))
            .Select(value => Id(value.id)).ToArray())).ToArray();
    }

    internal static string Id(ulong id) => id.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
