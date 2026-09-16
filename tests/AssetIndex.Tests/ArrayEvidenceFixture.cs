using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;

namespace AssetIndex.Tests;

internal static class ArrayEvidenceFixture
{
    public static ObjectEvidence Read(FPropertyTagType value) => EvidenceReader.Read(new UObject([
        new FPropertyTag { Name = "Data", PropertyType = value.GetType().Name, Tag = value }
    ])
    { Name = "Fixture", Outer = new ResolvedPackageObject(new FixturePackage { Name = "/Game/Fixture" }) });

    public sealed class NativeValue(object value) : FPropertyTagType
    {
        public override object GenericValue => value;
        public override string ToString() => "Native array fixture";
    }
}
