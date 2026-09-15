using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

internal sealed class FixturePackage(UObject export) : AbstractUePackage("Fixture", null)
{
    public override FPackageFileSummary Summary => throw new NotSupportedException();
    public override FNameEntrySerialized[] NameMap => [];
    public override int ImportMapLength => 0;
    public override int ExportMapLength => 1;
    public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
    public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 }
        ? new ResolvedLoadedObject(export) : null;
}
