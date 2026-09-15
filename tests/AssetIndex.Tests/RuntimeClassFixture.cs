using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

internal static class RuntimeClassFixture
{
    public static UBlueprintGeneratedClass Derive(UObject source, string? name = null)
    {
        var declaration = new UBlueprintGeneratedClass
        {
            Name = name ?? "Derived_" + source.ExportType,
            Outer = new ResolvedPackageObject(new FixturePackage { Name = "/Game/RuntimeClasses" }),
            Super = source.Class,
            SuperStruct = new FPackageIndex(new FixturePackage(source.Class!.Object!.Value), 1),
            ChildProperties = []
        };
        source.Class = new ResolvedLoadedObject(declaration);
        return declaration;
    }
}
