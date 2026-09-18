using System.Security.Cryptography;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;

namespace AssetIndex.Tests;

public sealed class ClassScopeTests
{
    private const string MappingHash = "61018eaea9fed46a1351fbdc5f7429e6f24df8f45fad576b4b15c534b1b359c4";
    private const string Sensing = "/Script/Angelscript.AISensingStatusTransition";
    private const string Widget = "/Script/UMG.WidgetBlueprintGeneratedClass";
    private const string Blueprint = "/Script/Engine.BlueprintGeneratedClass";
    private const string Runtime = "/Game/Widgets/Test.Test_C";
    private static readonly Lazy<TypeMappings> Bundled = new(() => new FileUsmapTypeMappingsProvider(
        Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Fact]
    public void PinnedNativeFamilyRequiresNoSyntheticSchema()
    {
        var mappings = Mappings();
        var count = mappings.Types.Count;
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
        Assert.Equal(MappingHash, Convert.ToHexStringLower(SHA256.HashData(stream)));

        Assert.True(new ClassScope(mappings, MappingHash).CanOmitBody(Sensing, _ => null));

        Assert.Equal(count, mappings.Types.Count);
        Assert.False(mappings.Types.ContainsKey("AISensingStatusTransition"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("changed")]
    public void NativeFamilyRejectsUnverifiedMapping(string? hash) =>
        Assert.False(new ClassScope(Mappings(), hash).CanOmitBody(Sensing, _ => null));

    [Theory]
    [InlineData("/Script/Other.AISensingStatusTransition")]
    [InlineData("/Script/Angelscript.AISensingStatusTransitionExtra")]
    [InlineData("/Script/Angelscript.AISensingStatusTransition:Child")]
    [InlineData("AISensingStatusTransition")]
    [InlineData("/Script/.AISensingStatusTransition")]
    [InlineData("/Script/Angelscript..AISensingStatusTransition")]
    public void NativeFamilyRequiresExactQualifiedIdentity(string path) =>
        Assert.False(new ClassScope(Mappings(), MappingHash).CanOmitBody(path, _ => null));

    [Theory]
    [InlineData("AIBSMTransitionInstance_Base", "DataAsset")]
    [InlineData("SMTransitionInstance", "SMTransitionInstance")]
    [InlineData("SMNodeInstance", null)]
    [InlineData("Object", "Unknown")]
    public void NativeFamilyRejectsContradictoryMappedContinuation(string name, string? parent)
    {
        var mappings = Mappings();
        mappings.Types[name] = new(mappings, name, parent, [], 0);

        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Sensing, _ => null));
    }

    [Fact]
    public void NativeFamilyRejectsMissingOrMisnamedMappedParent()
    {
        var mappings = Mappings();
        mappings.Types.Remove("SMNodeInstance");
        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Sensing, _ => null));
        mappings.Types["SMNodeInstance"] = new(mappings, "DifferentNode", "Object", [], 0);
        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Sensing, _ => null));
    }

    [Fact]
    public void ExistingNativeMappingMustBeDecodedEvenWhenItHasNoFields()
    {
        var mappings = Mappings();
        mappings.Types["AISensingStatusTransition"] = new(mappings, "AISensingStatusTransition", "AIBSMTransitionInstance_Base", [], 0);

        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Sensing, _ => null));
    }

    [Fact]
    public void WidgetClassBindingEstablishesFamilyWithoutInventingNativeParent()
    {
        var mappings = Mappings();
        var count = mappings.Types.Count;
        var declaration = new ClassScopeDeclaration(Widget, "/Script/Fixture.UnknownWidget", true);

        Assert.True(new ClassScope(mappings, null).CanOmitBody(Runtime, _ => declaration));

        Assert.Equal(count, mappings.Types.Count);
        Assert.False(mappings.Types.ContainsKey("UnknownWidget"));
    }

    [Theory]
    [InlineData("/Script/Other.WidgetBlueprintGeneratedClass")]
    [InlineData("WidgetBlueprintGeneratedClass")]
    [InlineData("/Game/Spoof.WidgetBlueprintGeneratedClass")]
    [InlineData(Blueprint)]
    public void MissingNativeParentIsNotClassifiedFromNamesOrAnOrdinaryBlueprint(string metaclass)
    {
        var declaration = new ClassScopeDeclaration(metaclass, "/Script/Fixture.DebugWidget", true);
        Assert.False(new ClassScope(Mappings(), MappingHash).CanOmitBody(Runtime, _ => declaration));
    }

    [Theory]
    [InlineData("/Script/Engine.DataAsset")]
    [InlineData("/Script/Engine.Actor")]
    [InlineData("/Script/UMG.UserWidget")]
    [InlineData("/Script/Fixture.UIMetaDataItem")]
    [InlineData(Sensing)]
    public void AvailableOrContradictoryWidgetParentsCannotBeOmitted(string parent)
    {
        var declaration = new ClassScopeDeclaration(Widget, parent, true);
        Assert.False(new ClassScope(Mappings(), MappingHash).CanOmitBody(Runtime, _ => declaration));
    }

    [Fact]
    public void WidgetFamilyRequiresItsMappedContinuation()
    {
        var mappings = Mappings();
        mappings.Types["Widget"] = new(mappings, "Widget", "DataAsset", [], 0);

        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Runtime,
            _ => new(Widget, "/Script/Fixture.UnknownWidget", true)));
    }

    [Theory]
    [InlineData("DataAsset")]
    [InlineData("DataTable")]
    [InlineData("CurveTable")]
    [InlineData("StringTable")]
    [InlineData("Blueprint")]
    [InlineData("BlueprintGeneratedClass")]
    public void WidgetBindingCannotHideAMissingCatalogRootMapping(string name)
    {
        var mappings = Mappings();
        mappings.Types.Remove(name);
        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Runtime,
            _ => new(Widget, "/Script/Engine." + name, true)));
    }

    [Theory]
    [InlineData(false, "/Script/Fixture.UnknownWidget")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "UnknownWidget")]
    [InlineData(true, "/Script/Fixture.Unknown.Widget")]
    public void MissingOrMalformedDeclarationCannotEstablishFamily(bool complete, string? parent) =>
        Assert.False(new ClassScope(Mappings(), MappingHash).CanOmitBody(Runtime, _ => new(Widget, parent, complete)));

    [Fact]
    public void RuntimeDescendantsFollowActualSuperPaths()
    {
        var parent = "/Game/Widgets/Base.Base_C";
        var declarations = new Dictionary<string, ClassScopeDeclaration>
        {
            [Runtime] = new(Blueprint, parent, true),
            [parent] = new(Widget, "/Script/Fixture.UnknownWidget", true)
        };
        var scope = new ClassScope(Mappings(), MappingHash);

        Assert.True(scope.CanOmitBody(Runtime, path => declarations.GetValueOrDefault(path)));
        declarations.Remove(parent);
        Assert.False(scope.CanOmitBody(Runtime, path => declarations.GetValueOrDefault(path)));
    }

    [Fact]
    public void RuntimeDescendantCanReachVerifiedNativeFamily() =>
        Assert.True(new ClassScope(Mappings(), MappingHash).CanOmitBody(Runtime, _ => new(Blueprint, Sensing, true)));

    [Fact]
    public void RuntimeShortNameDoesNotReplaceTheNativeNamespace()
    {
        var mappings = Mappings();
        mappings.Types["Test_C"] = new(mappings, "Test_C", "PersistenceDataAsset", [], 0);
        Assert.True(new ClassScope(mappings, MappingHash).CanOmitBody(Runtime,
            _ => new(Widget, "/Script/Fixture.UnknownWidget", true)));
    }

    [Fact]
    public void CyclicOrExcessiveRuntimeChainsFailClosed()
    {
        var scope = new ClassScope(Mappings(), MappingHash);
        Assert.False(scope.CanOmitBody(Runtime, _ => new(Widget, Runtime, true)));
        var count = 0;
        Assert.False(scope.CanOmitBody(Runtime, _ => new(Widget, $"/Game/Next.Class{++count}", true)));
        Assert.Equal(128, count);
    }

    [Fact]
    public void NativeMappedCyclesAndMissingAncestorsFailClosed()
    {
        var mappings = Mappings();
        mappings.Types["Cycle"] = new(mappings, "Cycle", "Cycle", [], 0);
        mappings.Types["Partial"] = new(mappings, "Partial", "AISensingStatusTransition", [], 0);
        var scope = new ClassScope(mappings, MappingHash);
        Assert.False(scope.CanOmitBody("/Script/Fixture.Cycle", _ => null));
        Assert.False(scope.CanOmitBody("/Script/Fixture.Partial", _ => null));
    }

    [Fact]
    public void WidgetBoundDoesNotHideAnIncompleteExistingNativeMapping()
    {
        var mappings = Mappings();
        mappings.Types["Partial"] = new(mappings, "Partial", "UnknownParent", [], 0);
        Assert.False(new ClassScope(mappings, MappingHash).CanOmitBody(Runtime,
            _ => new(Widget, "/Script/Fixture.Partial", true)));
    }

    private static TypeMappings Mappings() => new(new(Bundled.Value.Types, StringComparer.OrdinalIgnoreCase), Bundled.Value.Enums);
}
