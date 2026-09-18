using System.Security.Cryptography;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.GameTypes.Theia.Readers;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.VirtualFileSystem;

namespace AssetIndex;

// Mount completion order must not decide package identity. Equal-priority
// copies are interchangeable only after their data and decode context agree.
internal static class MountPrecedence
{
    internal readonly record struct ChunkKey(ulong Id, ushort Index, byte Padding, byte Type)
    {
        public static ChunkKey From(FIoChunkId chunk) => new(chunk.ChunkId, chunk._chunkIndex, chunk._padding, chunk.ChunkType);
    }

    public static IReadOnlyDictionary<GameFile, GameFile> Apply(FileProviderDictionary files, StringComparer comparer,
        Func<IoStoreReader, FIoStoreTocResource>? readToc = null)
    {
        var physical = files.Values.Distinct().ToArray();
        var physicalSet = physical.ToHashSet();
        if (files.ById.Any(pair => pair.Value is not FIoStoreEntry file || !IsNormalPackage(file) ||
            !pair.Key.Equals(file.ChunkId.AsPackageId()) || !physicalSet.Contains(file)))
            throw new InvalidDataException("Package ID index does not match the physical file inventory.");
        var proof = new Equivalence(readToc ?? ReadMetadata);
        var aliases = new Dictionary<GameFile, GameFile>();
        foreach (var file in physical.OfType<FIoStoreEntry>().Where(file => file.IsPackageData))
            proof.RequireOwnedStore(file);
        var winners = new Dictionary<string, GameFile>(comparer);
        var overlay = new Dictionary<string, GameFile>(comparer);
        foreach (var group in physical.GroupBy(file => file.Path, comparer))
        {
            var copies = group.ToArray();
            var winner = Choose(copies, proof, aliases);
            winners.Add(winner.Path, winner);
            if (copies.Length > 1) overlay.Add(winner.Path, winner);
        }

        var byId = new Dictionary<FPackageId, GameFile>();
        foreach (var group in physical.OfType<FIoStoreEntry>().Where(IsNormalPackage).GroupBy(file => file.ChunkId.AsPackageId()))
        {
            var copies = group.Cast<GameFile>().ToArray();
            var selected = Choose(copies, proof, aliases);
            if (copies.Any(file => !comparer.Equals(file.Path, selected.Path)) ||
                !ReferenceEquals(winners[selected.Path], selected))
                throw new InvalidDataException($"Package path and ID disagree at {selected.Path}.");
            if (!files.ById.TryGetValue(group.Key, out var current) || !ReferenceEquals(current, selected))
                byId.Add(group.Key, selected);
        }
        // Preserve original indexes for localization checks, payload ownership,
        // and upstream all-version import lookup. Do not rebuild or discard them.
        if (overlay.Count > 0 || byId.Count > 0) files.AddFiles(overlay, long.MaxValue, byId);
        return aliases;
    }

    private static bool IsNormalPackage(FIoStoreEntry file) =>
        file.IsPackageData && !file.IsOptionalPackage && file.ChunkId._chunkIndex == 0;

    internal static void RequirePackageIdentity(FIoStoreEntry file, string packageName)
    {
        if (!FPackageId.FromName(packageName).Equals(file.ChunkId.AsPackageId()))
            throw new InvalidDataException($"Decoded package name does not match its container identity: {file.Path}.");
    }

    private static GameFile Choose(GameFile[] copies, Equivalence proof, Dictionary<GameFile, GameFile> aliases)
    {
        if (copies.Length == 1) return copies[0];
        if (copies.Any(file => file is not VfsEntry))
            throw new InvalidDataException($"Duplicate input has no known archive priority: {copies[0].Path}.");
        var tiers = copies.Cast<VfsEntry>().GroupBy(file => file.Vfs.ReadOrder).OrderByDescending(group => group.Key).ToArray();
        GameFile? winner = null;
        foreach (var tier in tiers)
        {
            var peers = tier.OrderBy(file => file.Vfs.Path, StringComparer.Ordinal)
                .ThenBy(file => file.Path, StringComparer.Ordinal).ToArray();
            foreach (var peer in peers.Skip(1))
            {
                proof.RequireEqual(peers[0], peer);
                aliases[peer] = peers[0];
            }
            winner ??= peers[0];
        }
        return winner!;
    }

    private static FIoStoreTocResource ReadMetadata(IoStoreReader reader)
    {
        using FArchive archive = File.Exists(reader.Path + ".meta")
            ? new FTheiaArchive(reader.Path, reader.Versions)
            : new FRandomAccessFileStreamArchive(reader.Path, reader.Versions);
        // ReadDirectoryIndex leaves the upstream cursor at the directory start;
        // metadata-only reading advances over it before reading chunk hashes.
        return new FIoStoreTocResource(archive, EIoStoreTocReadOptions.ReadTocMeta);
    }

    internal static GameFile? OwnedPayload(VfsEntry file, string extension) =>
        file.Vfs.Files.GetValueOrDefault(file.PathWithoutExtension + extension);

    internal static (GameFile? Bulk, GameFile? Optional) ResolvePayloads(FIoStoreEntry file, FileProviderDictionary files)
    {
        GameFile? Resolve(string extension) => OwnedPayload(file, extension) ??
            files.GetValueOrDefault(file.PathWithoutExtension + extension);
        return (Resolve(".ubulk"), Resolve(".uptnl"));
    }

    private sealed class Equivalence(Func<IoStoreReader, FIoStoreTocResource> readToc)
    {
        private readonly Dictionary<IoStoreReader, Dictionary<ChunkKey, ChunkContent>> metadata = new();
        private readonly Dictionary<IoStoreReader, Dictionary<ulong, Dictionary<ChunkKey, ChunkContent>>> payloads = new();
        private readonly Dictionary<IoStoreReader, Dictionary<FPackageId, FFilePackageStoreEntry>> stores = new();
        private readonly Dictionary<GameFile, string> rawHashes = new();

        public void RequireEqual(VfsEntry left, VfsEntry right)
        {
            if (!EqualContent(left, right)) Fail(left, right, "content differs");
            if (!left.IsUePackage && !right.IsUePackage) return;
            if (left.IsUePackage != right.IsUePackage) Fail(left, right, "package kinds differ");
            foreach (var extension in new[] { ".uexp", ".ubulk", ".uptnl" })
            {
                var first = OwnedPayload(left, extension);
                var second = OwnedPayload(right, extension);
                if ((first is null) != (second is null) || first is not null && !EqualContent(first, second!))
                    Fail(left, right, "owned payloads differ");
            }
            if (left is FIoStoreEntry firstIo && right is FIoStoreEntry secondIo)
            {
                RequireStore(firstIo, secondIo);
                var first = Payloads(firstIo);
                var second = Payloads(secondIo);
                if (first.Count != second.Count || first.Any(pair => second.GetValueOrDefault(pair.Key) != pair.Value))
                    Fail(left, right, "package bulk chunks differ");
            }
            else if (left is FIoStoreEntry || right is FIoStoreEntry)
                Fail(left, right, "package formats differ");
        }

        private bool EqualContent(GameFile left, GameFile right)
        {
            if (left.Size != right.Size) return false;
            if (left is FIoStoreEntry first && right is FIoStoreEntry second)
                return ChunkKey.From(first.ChunkId) == ChunkKey.From(second.ChunkId) && Content(first) == Content(second);
            if (left is FIoStoreEntry || right is FIoStoreEntry) return false;
            return Hash(left) == Hash(right);
        }

        private string Hash(GameFile file)
        {
            if (rawHashes.TryGetValue(file, out var hash)) return hash;
            var bytes = file.Read();
            if (bytes.LongLength != file.Size) throw new InvalidDataException($"Incomplete duplicate input: {file.Path}.");
            return rawHashes[file] = Convert.ToHexString(SHA256.HashData(bytes));
        }

        private ChunkContent Content(FIoStoreEntry file)
        {
            var chunks = Chunks(file.IoStoreReader);
            if (!chunks.TryGetValue(ChunkKey.From(file.ChunkId), out var content) || content.Length != (ulong)file.Size)
                throw new InvalidDataException($"Duplicate input has no matching chunk: {file.Path}.");
            RequireHash(content);
            return content;
        }

        private Dictionary<ChunkKey, ChunkContent> Payloads(FIoStoreEntry file)
        {
            _ = Chunks(file.IoStoreReader);
            var result = payloads[file.IoStoreReader].GetValueOrDefault(file.ChunkId.ChunkId) ?? [];
            foreach (var content in result.Values) RequireHash(content);
            return result;
        }

        private Dictionary<ChunkKey, ChunkContent> Chunks(IoStoreReader reader)
        {
            if (metadata.TryGetValue(reader, out var cached)) return cached;
            var mounted = reader.TocResource;
            var toc = readToc(reader);
            if (toc.ChunkMetas is null || toc.ChunkMetas.Length != mounted.ChunkIds.Length ||
                toc.ChunkIds.Length != mounted.ChunkIds.Length || toc.ChunkOffsetLengths.Length != mounted.ChunkOffsetLengths.Length ||
                toc.Header.Version != mounted.Header.Version || !toc.Header.ContainerId.Equals(mounted.Header.ContainerId))
                throw new InvalidDataException($"Duplicate input chunk metadata is unavailable or changed: {reader.Name}.");
            var result = new Dictionary<ChunkKey, ChunkContent>();
            for (var index = 0; index < toc.ChunkIds.Length; index++)
            {
                var key = ChunkKey.From(toc.ChunkIds[index]);
                var location = toc.ChunkOffsetLengths[index];
                if (key != ChunkKey.From(mounted.ChunkIds[index]) ||
                    location.Offset != mounted.ChunkOffsetLengths[index].Offset || location.Length != mounted.ChunkOffsetLengths[index].Length)
                    throw new InvalidDataException($"Duplicate input chunk metadata changed: {reader.Name}.");
                if (!result.TryAdd(key, new(location.Length, toc.ChunkMetas[index].ChunkHash.ToString())))
                    throw new InvalidDataException($"Duplicate exact chunk identity: {reader.Name}.");
            }
            payloads[reader] = result.Where(pair => pair.Key.Type is (byte)EIoChunkType5.BulkData or
                (byte)EIoChunkType5.OptionalBulkData or (byte)EIoChunkType5.MemoryMappedBulkData)
                .GroupBy(pair => pair.Key.Id).ToDictionary(group => group.Key, group => group.ToDictionary());
            return metadata[reader] = result;
        }

        private static void RequireHash(ChunkContent content)
        {
            if (content.Hash.Length != 40 || content.Hash.All(character => character == '0'))
                throw new InvalidDataException("Duplicate input has no usable chunk hash.");
        }

        private void RequireStore(FIoStoreEntry left, FIoStoreEntry right)
        {
            var first = Store(left);
            var second = Store(right);
            if (left.IoStoreReader.ContainerHeader!.Version != right.IoStoreReader.ContainerHeader!.Version ||
                first.ExportCount != second.ExportCount || first.ExportBundleCount != second.ExportBundleCount ||
                !first.ImportedPackages.SequenceEqual(second.ImportedPackages))
                Fail(left, right, "package-store context differs");
        }

        public void RequireOwnedStore(FIoStoreEntry file) => _ = Store(file);

        private FFilePackageStoreEntry Store(FIoStoreEntry file)
        {
            var header = file.IoStoreReader.ContainerHeader ?? throw new InvalidDataException("Package has no container header.");
            if (!stores.TryGetValue(file.IoStoreReader, out var index))
            {
                index = Index(header.PackageIds, header.StoreEntries);
                // Follow IoPackage's exact normal-before-optional store selection.
                foreach (var pair in Index(header.OptionalSegmentPackageIds, header.OptionalSegmentStoreEntries))
                    index.TryAdd(pair.Key, pair.Value);
                stores.Add(file.IoStoreReader, index);
            }
            if (!index.TryGetValue(file.ChunkId.AsPackageId(), out var store) || store.ImportedPackages is null)
                throw new InvalidDataException($"Package has no unique owned store context: {file.Path}.");
            return store;
        }

        private static Dictionary<FPackageId, FFilePackageStoreEntry> Index(FPackageId[]? ids, FFilePackageStoreEntry[]? entries)
        {
            if (ids is null && entries is null) return [];
            if (ids is null || entries is null || ids.Length != entries.Length)
                throw new InvalidDataException("Package-store index is inconsistent.");
            var result = new Dictionary<FPackageId, FFilePackageStoreEntry>();
            for (var index = 0; index < ids.Length; index++)
                if (entries[index] is null || !result.TryAdd(ids[index], entries[index]))
                    throw new InvalidDataException("Package-store index is not unique.");
            return result;
        }

        private static void Fail(GameFile left, GameFile right, string reason) =>
            throw new InvalidDataException($"Ambiguous equal-priority input {left.Path}: {reason} ({((VfsEntry)left).Vfs.Name}, {((VfsEntry)right).Vfs.Name}).");

        private sealed record ChunkContent(ulong Length, string Hash);
    }
}
