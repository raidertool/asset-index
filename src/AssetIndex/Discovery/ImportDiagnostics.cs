using System.Text;
using System.Text.Json;
using CUE4Parse.UE4.Assets;
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
}
