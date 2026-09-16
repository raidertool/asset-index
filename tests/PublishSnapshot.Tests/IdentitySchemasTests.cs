using System.Security.Cryptography;
using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class IdentitySchemasTests
{
    private const string Instance = "/Game/Item.Item";
    private const string Runtime = "/Game/Type.Type_C";

    [Fact]
    public void RuntimeInheritanceUsesQualifiedHeaderAndBodySuperclasses()
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Instance, "Type_C", classPath: Runtime);
        Number(source, "AssetId", "42");
        RuntimeClass(fixture, Runtime, "/Script/Test.PersistenceDataAsset");
        var context = fixture.Read().Context;
        Validate(context, Catalog("42", [source]));
        var schema = context.Schema(Instance);
        Assert.Equal([Runtime, "/Script/Test.PersistenceDataAsset"], schema.QualifiedAncestry);
        Assert.Equal(["PersistenceDataAsset", "DataAsset", "Object"], schema.NativeAncestry);
    }

    [Fact]
    public void RuntimeDeclaredIdentityPropertyMustHaveScalarInt64Type()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("NativePersistence", "OptionalPersistenceDataAsset");
        // A native base declaring AssetId plus a runtime duplicate is ambiguous,
        // even when the body values happen to agree.
        var source = fixture.Object(Instance, "Type_C", classPath: Runtime);
        Number(source, "AssetId", "42");
        var declaration = RuntimeClass(fixture, Runtime, "/Script/Test.NativePersistence", ("AssetId", "FInt64Property", "1"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
        declaration["values"]!.AsArray().Clear();
        Value(declaration, "/Native/ChildProperties", "FField[]", "empty-array", null);
        Validate(fixture.Read().Context, Catalog("42", [source]));
    }

    [Theory]
    [InlineData("header-parent")]
    [InlineData("missing-body")]
    [InlineData("missing-declarations")]
    [InlineData("ambiguous-declarations")]
    [InlineData("cycle")]
    [InlineData("null-parent")]
    [InlineData("wrong-class")]
    public void IncompleteRuntimeClassCannotAssertNativeIdentityRole(string mutation)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Instance, "Type_C", classPath: Runtime);
        Number(source, "AssetId", "42");
        var declaration = RuntimeClass(fixture, Runtime, "/Script/Test.PersistenceDataAsset");
        switch (mutation)
        {
            case "header-parent": fixture.Exports[1]["superPath"] = "/Script/Test.Other"; break;
            case "missing-body": fixture.Objects.Remove(declaration); break;
            case "missing-declarations": declaration["values"]!.AsArray().Clear(); break;
            case "ambiguous-declarations":
                Value(declaration, "/Native/ChildProperties", "FField[]", "empty-array", null); break;
            case "cycle":
                declaration["references"]![2]!["targetPath"] = Runtime;
                fixture.Exports[1]["superPath"] = Runtime;
                break;
            case "null-parent":
                declaration["references"]![2]!["targetPath"] = null;
                declaration["references"]![2]!["isNull"] = true;
                fixture.Exports[1]["superPath"] = null;
                break;
            case "wrong-class": source["class"] = "Other_C"; break;
        }
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }

    [Fact]
    public void UnrelatedFunctionDeclarationDoesNotNeedAClassSuperclass()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("Function", "Struct");
        var function = fixture.Object("/Game/Type.Type_C:Run", "Function");
        function["references"]!.AsArray().Add(Link("/Native/SuperStruct", null, "super", "hard"));
        Value(function, "/Native/ChildProperties", "FField[]", "empty-array", null);
        var source = fixture.Object(Instance, "PersistenceDataAsset");
        Number(source, "AssetId", "42");
        Validate(fixture.Read().Context, Catalog("42", [source]));
    }

    [Fact]
    public void RuntimeClassNamedAfterNativePersistenceDoesNotAcquireItsRole()
    {
        using var fixture = new IdentityFixture();
        const string runtime = "/Game/Type.PersistenceDataAsset";
        var source = fixture.Object(Instance, "PersistenceDataAsset", classPath: runtime);
        Number(source, "AssetId", "42");
        RuntimeClass(fixture, runtime, "/Script/Test.DataAsset", ("AssetId", "FInt64Property", "1"));
        var context = fixture.Read().Context;
        Assert.False(context.Schema(Instance).IsA("PersistenceDataAsset"));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [source])));
    }

    [Fact]
    public void NativeDerivedClassCanInheritIdentityFromItsMappedNativeBaseTemplate()
    {
        using var fixture = new IdentityFixture();
        const string template = "/Game/Base.Base";
        fixture.AddClass("DerivedPersistence", "PersistenceDataAsset");
        var source = fixture.Object(Instance, "DerivedPersistence", template);
        Number(fixture.Object(template, "PersistenceDataAsset"), "AssetId", "42");
        Validate(fixture.Read().Context, Catalog("42", [source]));
    }

    [Fact]
    public void RuntimeClassWithNativeBaseNameCannotImpersonateANativeTemplate()
    {
        using var fixture = new IdentityFixture();
        const string template = "/Game/Base.Base";
        const string impostor = "/Game/Type.PersistenceDataAsset";
        fixture.AddClass("DerivedPersistence", "PersistenceDataAsset");
        var source = fixture.Object(Instance, "DerivedPersistence", template);
        Number(fixture.Object(template, "PersistenceDataAsset", classPath: impostor), "AssetId", "42");
        RuntimeClass(fixture, impostor, "/Script/Test.DataAsset", ("AssetId", "FInt64Property", "1"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }

    [Theory]
    [InlineData("missing-name")]
    [InlineData("wrong-name-kind")]
    [InlineData("missing-array")]
    [InlineData("noncanonical-array")]
    [InlineData("noncontiguous")]
    public void RuntimeDeclarationsRetainTheirTypedStructure(string mutation)
    {
        using var fixture = new IdentityFixture();
        fixture.Object(Instance, "Type_C", classPath: Runtime);
        var declaration = RuntimeClass(fixture, Runtime, "/Script/Test.PersistenceDataAsset", ("Other", "FBoolProperty", "1"));
        var values = declaration["values"]!.AsArray();
        switch (mutation)
        {
            case "missing-name": values.RemoveAt(1); break;
            case "wrong-name-kind": values[1]!["kind"] = "string"; break;
            case "missing-array": values.RemoveAt(2); break;
            case "noncanonical-array": values[2]!["value"] = "+1"; break;
            case "noncontiguous":
                foreach (var value in values) value!["pointer"] = value["pointer"]!.GetValue<string>().Replace("/0", "/1", StringComparison.Ordinal);
                break;
        }
        Assert.Throws<InvalidDataException>(() => fixture.Read().Context.Schema(Instance));
    }

    [Fact]
    public void ClassDefaultUsesExplicitClassReference()
    {
        using var fixture = new IdentityFixture();
        var declaration = RuntimeClass(fixture, Runtime, "/Script/Test.PersistenceDataAsset");
        declaration["references"]!.AsArray().Add(Link("/Native/ClassDefaultObject", Instance, "class-default", "hard"));
        var context = fixture.Read().Context;
        Assert.True(context.IsClassDefault(Instance));
        Assert.False(context.IsClassDefault("/Game/Fake.Default__Fake"));
    }

    [Fact]
    public void ClassDefaultCannotBePublishedAsItsOwnAssetDefinition()
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Instance, "Type_C", classPath: Runtime);
        Number(source, "AssetId", "42");
        var declaration = RuntimeClass(fixture, Runtime, "/Script/Test.PersistenceDataAsset");
        declaration["references"]!.AsArray().Add(Link("/Native/ClassDefaultObject", Instance, "class-default", "hard"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }

    [Fact]
    public void MappingBytesMustMatchRecordedHashBeforeParsing()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "not a mapping");
            var error = Assert.Throws<InvalidDataException>(() => IdentitySchemas.LoadMappings(file, new string('0', 64)));
            Assert.Contains("mapping hash", error.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void BundledPinnedMappingCanBeLoadedThroughUpstreamParser()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        var mappings = IdentitySchemas.LoadMappings(path, hash);
        Assert.Contains("PersistenceDataAsset", mappings.Types.Keys);
        Assert.Contains("ItemDataAssetBase", mappings.Types.Keys);
    }

    private static JsonObject RuntimeClass(IdentityFixture fixture, string path, string parent,
        params (string Name, string Type, string Array)[] fields)
    {
        var declaration = fixture.Object(path, "BlueprintGeneratedClass");
        fixture.Exports[^1]["superPath"] = parent;
        declaration["references"]!.AsArray().Add(Link("/Native/SuperStruct", parent, "super", "hard"));
        if (fields.Length == 0) Value(declaration, "/Native/ChildProperties", "FField[]", "empty-array", null);
        for (var index = 0; index < fields.Length; index++)
        {
            var prefix = "/Native/ChildProperties/" + index;
            Value(declaration, prefix, fields[index].Type, "field-declaration", null);
            Value(declaration, prefix + "/Name", "FName", "name", fields[index].Name);
            Value(declaration, prefix + "/ArrayDim", "Int32", "integer", fields[index].Array);
        }
        return declaration;
    }
}
