using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

internal sealed class FixturePackage(params UObject[] exports) : AbstractUePackage("Fixture", null)
{
    public override FPackageFileSummary Summary => throw new NotSupportedException();
    public override FNameEntrySerialized[] NameMap => [];
    public override int ImportMapLength => 0;
    public override int ExportMapLength => exports.Length;
    public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) =>
        Array.FindIndex(exports, export => export.Name.Equals(name, comparisonType));
    public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: > 0 } && index.Index <= exports.Length
        ? new ResolvedLoadedObject(exports[index.Index - 1]) : null;
}
