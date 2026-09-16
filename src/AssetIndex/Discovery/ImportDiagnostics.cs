using System.Text;
using System.Text.Json;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal static class ImportDiagnostics
{
    public static string Describe(FPackageIndex reference, string error)
    {
        if (reference.Owner is not IoPackage package || !reference.IsImport) return error;
        var slot = -(long)reference.Index - 1;
        var details = new StringBuilder(error).Append($" Io import: mapSlot={slot}");
        if (slot >= package.ImportMap.Length)
            return details.Append($" outside importMapLength={package.ImportMap.Length}.").ToString();
        var index = package.ImportMap[(int)slot];
        details.Append($"; objectIndex=0x{index.TypeAndId:X16}; type={index.Type}");
        if (!index.IsPackageImport) return details.Append('.').ToString();
        if (package.ImportedPublicExportHashes is not { } hashes)
            return details.Append("; format=global-import-index.").ToString();
        var import = index.AsPackageImportRef;
        details.Append($"; packageSlot={import.ImportedPackageIndex}; hashSlot={import.ImportedPublicExportHashIndex}; expectedHash=");
        details.Append(import.ImportedPublicExportHashIndex < hashes.Length
            ? $"0x{hashes[import.ImportedPublicExportHashIndex]:X16}"
            : $"out-of-range(length={hashes.Length})");
        details.Append("; packageIdentity=").Append(PackageIdentity(package, import.ImportedPackageIndex));
        // Resolution has already run. Observing diagnostics must not evaluate any
        // additional package or body lazy, including one that previously failed.
        details.Append("; winner=");
        if (!package.ImportedPackages.IsValueCreated) details.Append("unavailable");
        else
        {
            var winners = package.ImportedPackages.Value;
            details.Append(import.ImportedPackageIndex < winners.Length
                ? DescribePackage(winners[import.ImportedPackageIndex]) : $"out-of-range(length={winners.Length})");
        }
        details.Append("; alternatives=");
        if (!package.ImportedPackagesAllVersions.IsValueCreated) details.Append("unavailable");
        else
        {
            var alternatives = package.ImportedPackagesAllVersions.Value;
            details.Append(import.ImportedPackageIndex < alternatives.Length
                ? "[" + string.Join(",", alternatives[import.ImportedPackageIndex].Select(DescribePackage)) + "]"
                : $"out-of-range(length={alternatives.Length})");
        }
        return details.Append('.').ToString();
    }

    private static string DescribePackage(IPackage? package) => package is null ? "null"
        : $"{{name={JsonSerializer.Serialize(package.Name)},exports={package.ExportMapLength}}}";

    private static string PackageIdentity(IoPackage package, uint slot)
    {
        if (package.Provider is not PackageProvider provider || provider.SourceFile(package) is not FIoStoreEntry source)
            return "unavailable(untracked IoStore source)";
        try
        {
            // PackageProvider already consumed this exact reader's container header
            // to construct the owner. Do not resolve a file by logical package name.
            return DescribePackageIdentity(source, package.Name, slot, provider.FilesById);
        }
        catch (Exception error) { return $"unavailable({JsonSerializer.Serialize(error.Message)})"; }
    }

    internal static string DescribePackageIdentity(FIoStoreEntry source, string packageName, uint slot,
        IReadOnlyDictionary<FPackageId, GameFile> indexed)
    {
        var header = source.IoStoreReader.ContainerHeader;
        if (header is null) return "unavailable(no owning container header)";
        var id = FPackageId.FromName(packageName);
        var index = Array.IndexOf(header.PackageIds, id);
        var kind = "normal";
        var entries = header.StoreEntries;
        if (index < 0)
        {
            index = Array.IndexOf(header.OptionalSegmentPackageIds ?? [], id);
            kind = "optional";
            entries = header.OptionalSegmentStoreEntries;
        }
        if (index < 0) return "unavailable(no direct store entry; fallback imports not inspected)";
        if (entries is null || index >= entries.Length || entries[index]?.ImportedPackages is not { } imports)
            return "unavailable(invalid direct store entry)";
        var origin = $"{{file={JsonSerializer.Serialize(source.Path)},container={JsonSerializer.Serialize(source.IoStoreReader.Path)},packageId=0x{id.id:X16},store={kind},index={index}}}";
        if (slot >= imports.Length) return $"{{source={origin},slot={slot},error=out-of-range(length={imports.Length})}}";
        var imported = imports[slot];
        var target = indexed.TryGetValue(imported, out var file) ? DescribeFile(file) : "none";
        return $"{{source={origin},slot={slot},importedPackageId=0x{imported.id:X16},indexedTarget={target}}}";
    }

    private static string DescribeFile(GameFile file) =>
        $"{{file={JsonSerializer.Serialize(file.Path)},container={JsonSerializer.Serialize((file as FIoStoreEntry)?.IoStoreReader.Path)}}}";
}
