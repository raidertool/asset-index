using System.Reflection;
using System.Runtime.CompilerServices;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

internal static class IoMetadataFixture
{
    public static IoPackage Create(FExportMapEntry[] entries, string[] names,
        Dictionary<FPackageObjectIndex, FScriptObjectEntry> scripts, Action payloadRead,
        FPackageObjectIndex[]? imports = null)
    {
        // Install parsed headers to exercise CUE's real metadata resolution,
        // without claiming to test mounting or container deserialization.
        var package = (IoPackage)RuntimeHelpers.GetUninitializedObject(typeof(IoPackage));
        package.Name = "/Game/Map";
        Set(typeof(IoPackage), package, "ExportMap", entries);
        Set(typeof(IoPackage), package, "ImportMap", imports ?? []);
        Set(typeof(IoPackage), package, "<NameMap>k__BackingField", names.Select(name => new FNameEntrySerialized(name)).ToArray());
        var globals = (IoGlobalData)RuntimeHelpers.GetUninitializedObject(typeof(IoGlobalData));
        Set(typeof(IoGlobalData), globals, "ScriptObjectEntriesMap", scripts);
        Set(typeof(IoPackage), package, "_globalData", globals);
        Set(typeof(AbstractUePackage), package, "<ExportsLazy>k__BackingField", entries.Select(_ => new Lazy<UObject>(() =>
        {
            payloadRead();
            throw new InvalidOperationException("Metadata inspection must not read an instance payload.");
        })).ToArray());
        return package;
    }

    public static FExportMapEntry Entry(int name, ulong type, ulong? super = null, ulong? outer = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0UL); writer.Write(0UL); writer.Write((uint)name); writer.Write(0U);
            writer.Write(outer ?? FPackageObjectIndex.Invalid); writer.Write(type);
            writer.Write(super ?? FPackageObjectIndex.Invalid); writer.Write(FPackageObjectIndex.Invalid);
            writer.Write(0UL); writer.Write(0U); writer.Write(0U);
        }
        using var archive = new FByteArchive("Synthetic export header", stream.ToArray(), new VersionContainer(EGame.GAME_ArcRaiders));
        return new FExportMapEntry(archive);
    }

    public static FScriptObjectEntry ScriptEntry(uint name, ulong index, ulong outer)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(name); writer.Write(0U); writer.Write(index);
            writer.Write(outer); writer.Write(FPackageObjectIndex.Invalid);
        }
        using var archive = new FByteArchive("Synthetic native script entry", stream.ToArray());
        return archive.Read<FScriptObjectEntry>();
    }

    private static void Set(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
}
