using System.Runtime.CompilerServices;
using AssetIndex.Discovery;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;

namespace AssetIndex.Tests;

public sealed class EvidenceTests
{
    private static readonly ResolvedPackageObject Root = new(new TestPackage(null, EPackageFlags.PKG_UnversionedProperties));
    [Fact]
    public void ReadsTypedNestedValuesWithOrdinalPointersAndExactNames()
    {
        var nested = new FStructFallback([
            Property("A/B~C", new Int64Property(long.MinValue)),
            Property("Unsigned", new ValueProperty(ulong.MaxValue)),
            Property("Label", new TextProperty(new FText("Items", "Name", "Actual", "Cached translation")))
        ]);
        var source = Object("Item", Property("Nested", new StructProperty(new FScriptStruct(nested))));

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/Properties/0" && value.Type == "Int64Property" && value.Value == "-9223372036854775808");
        Assert.Contains(evidence.Values, value => value.Value == "18446744073709551615" && value.Kind == "integer");
        var text = Assert.Single(evidence.Texts);
        Assert.Equal("/Properties/0/Properties/2", text.Pointer);
        Assert.Contains(evidence.Properties, property => property.Pointer == "/Properties/0/Properties/0" && property.Name == "A/B~C");
        Assert.Equal("Actual", text.Source);
        Assert.DoesNotContain(evidence.Values, value => value.Value == "Cached translation");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void PreservesMapKeysAndValuesWithoutTurningStringsIntoReferences()
    {
        var map = new UScriptMap(new()
        {
            [new StrProperty("/Game/LooksLikeAPath.Asset")] = new ObjectProperty(new FPackageIndex()),
            [new Int64Property(-42)] = new TextProperty(new FText("A value"))
        });
        var evidence = EvidenceReader.Read(Object("Map", Property("Data", new MapProperty(map))));

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/entries/0/key" && value.Kind == "string");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/entries/1/key" && value.Value == "-42");
        Assert.DoesNotContain(evidence.References, reference => reference.TargetPath == "/Game/LooksLikeAPath.Asset");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/0/entries/0/value" && reference.IsNull);
        Assert.Equal("/Properties/0/entries/1/value", Assert.Single(evidence.Texts).Pointer);
    }

    [Fact]
    public void EmptyAndNullOverridesRemainDirectAndDoNotReadTheTemplate()
    {
        var parent = new NeverLoadedReference("Parent");
        var source = Object("Child",
            Property("Array", new ArrayProperty(new UScriptArray("TextProperty"))),
            Property("Set", new ValueProperty(new UScriptSet())),
            Property("Map", new MapProperty(new UScriptMap())),
            Property("Empty", new StrProperty("")),
            Property("Null", new ObjectProperty(new FPackageIndex())),
            Property("Enabled", new BoolProperty(false)));
        source.Template = parent;

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0" && value.Kind == "empty-array");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/1" && value.Kind == "empty-set");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/2" && value.Kind == "empty-map");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/3" && value.Value == "");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/5" && value.Value == "false");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Template" && reference.Role == "template" && reference.TargetPath == "/Game/Fixture.Parent");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/4" && reference.IsNull);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void TypedReferencesPreserveNullFailuresAndSubobjectPathsWithoutLoading()
    {
        var target = new NeverLoadedReference("Target");
        var package = new TestPackage(target);
        var source = Object("Refs",
            Property("Hard", new ObjectProperty(new FPackageIndex(package, 1))),
            Property("Broken", new ObjectProperty(new FPackageIndex(package, 2))),
            Property("Resolved", new ValueProperty(target)),
            Property("Soft", new ValueProperty(new FSoftObjectPath("/Game/A.A", "Child"))));

        var evidence = EvidenceReader.Read(source);

        var hard = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/0");
        Assert.Equal("/Game/Fixture.Target", hard.TargetPath);
        Assert.Equal(1, hard.PackageIndex);
        Assert.Null(hard.Error);
        var broken = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/1");
        Assert.False(broken.IsNull);
        Assert.Null(broken.TargetPath);
        Assert.NotNull(broken.Error);
        Assert.Contains(evidence.References, reference => reference.Kind == "resolved" && reference.TargetPath == "/Game/Fixture.Target");
        Assert.Contains(evidence.References, reference => reference.Kind == "soft" && reference.TargetPath == "/Game/A.A:Child");
    }

    [Fact]
    public void NativeDelegatesPreserveBindingAndFunctionWithoutLoadingTargets()
    {
        var package = new TestPackage(new NeverLoadedReference("Handler"));
        var callback = new FScriptDelegate(new FPackageIndex(package, 1), "OnChanged");
        var multicast = new FMulticastScriptDelegate([
            callback, new(new FPackageIndex(package, 2), "MissingHandler"), new(new FPackageIndex(), "None")
        ]);
        var evidence = EvidenceReader.Read(Object("Delegates",
            Property("Single", new DelegateProperty(callback)),
            Property("Many", new MulticastDelegateProperty(multicast))));

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/0/Object" && reference.TargetPath == "/Game/Fixture.Handler");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/FunctionName" && value.Value == "OnChanged");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/1/InvocationList/0/Object" && reference.TargetPath == "/Game/Fixture.Handler");
        var broken = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/1/InvocationList/1/Object");
        Assert.False(broken.IsNull);
        Assert.NotNull(broken.Error);
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/1/InvocationList/2/Object" && reference.IsNull);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void EmptyMulticastDelegateRemainsExplicit()
    {
        var evidence = EvidenceReader.Read(Object("Empty", Property("Callback", new MulticastDelegateProperty(new FMulticastScriptDelegate([])))));

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/InvocationList" && value.Kind == "empty-array");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void NativeFieldPathsPreserveNamesAndUnresolvedOwner()
    {
        var package = new TestPackage(new NeverLoadedReference("Unused"));
        var path = new FFieldPath { Path = ["Target", "Nested"], ResolvedOwner = new FPackageIndex(package, 2) };
        var evidence = EvidenceReader.Read(Object("Fields", Property("Field", new FieldPathProperty(path))));

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/Path/0" && value.Value == "Target");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0/Path/1" && value.Value == "Nested");
        var owner = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/0/ResolvedOwner");
        Assert.False(owner.IsNull);
        Assert.Equal(2, owner.PackageIndex);
        Assert.NotNull(owner.Error);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void NativeInterfaceReferencesPreserveBoundAndNullValues()
    {
        var package = new TestPackage(new NeverLoadedReference("Implementation"));
        var evidence = EvidenceReader.Read(Object("Interfaces",
            Property("Bound", new InterfaceProperty(new FScriptInterface(new FPackageIndex(package, 1)))),
            Property("Null", new InterfaceProperty(new FScriptInterface(new FPackageIndex())))));

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/0/Object" && reference.TargetPath == "/Game/Fixture.Implementation");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/1/Object" && reference.IsNull);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void LazyObjectGuidRemainsAnIdentifierWithoutInventingAPath()
    {
        var guid = new FUniqueObjectGuid { Guid = new FGuid(1, 2, 3, uint.MaxValue) };
        var evidence = EvidenceReader.Read(Object("Lazy", Property("Object", new LazyObjectProperty(guid))));

        Assert.Equal(["1", "2", "3", "4294967295"], evidence.Values
            .Where(value => value.Pointer.StartsWith("/Properties/0/Guid/", StringComparison.Ordinal)).Select(value => value.Value));
        Assert.DoesNotContain(evidence.References, reference => reference.Pointer.StartsWith("/Properties/0", StringComparison.Ordinal));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void CompositeTextKeepsNestedHistoryAndInvariantMetadata()
    {
        var invariant = Make<FTextHistory.None>((nameof(FTextHistory.None.CultureInvariantString), "Invariant"));
        var argument = Make<FFormatArgumentValue>((nameof(FFormatArgumentValue.Type), EFormatArgumentType.Text),
            (nameof(FFormatArgumentValue.Value), new FText((uint)ETextFlag.CultureInvariant, ETextHistoryType.None, invariant)));
        var history = Make<FTextHistory.NamedFormat>(
            (nameof(FTextHistory.NamedFormat.SourceFmt), new FText("Fmt", "Count", "{item}")),
            (nameof(FTextHistory.NamedFormat.Arguments), new Dictionary<string, FFormatArgumentValue> { ["item"] = argument }));
        var evidence = EvidenceReader.Read(Object("Text", Property("Label", new TextProperty(new FText(0, ETextHistoryType.NamedFormat, history)))));

        Assert.Equal(3, evidence.Texts.Count);
        Assert.Contains(evidence.Texts, text => text.Pointer == "/Properties/0" && text.History == "NamedFormat");
        Assert.Contains(evidence.Texts, text => text.Pointer == "/Properties/0/History/SourceFmt" && text.Source == "{item}" && text.Key == "Count");
        Assert.Contains(evidence.Texts, text => text.Source == "Invariant" && text.Flags == (uint)ETextFlag.CultureInvariant);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void StringTableReferencesAndEntriesRemainDistinctEvidence()
    {
        var history = Make<FTextHistory.StringTableEntry>((nameof(FTextHistory.StringTableEntry.TableId), new FName("/Game/ST_Items.ST_Items")),
            (nameof(FTextHistory.StringTableEntry.Key), "NAME"), (nameof(FTextHistory.StringTableEntry.SourceString), "Stale previous build"));
        var reference = EvidenceReader.Read(Object("Ref", Property("Label", new TextProperty(new FText(0, ETextHistoryType.StringTableEntry, history)))));
        var text = Assert.Single(reference.Texts);
        Assert.Equal("/Game/ST_Items.ST_Items", text.TableId);
        Assert.Null(text.Namespace);
        Assert.Null(text.Source);
        var table = new UStringTable { Name = "ST_Items", Outer = Root };
        var data = Make<FStringTable>((nameof(FStringTable.TableNamespace), "Items"),
            (nameof(FStringTable.KeysToEntries), new Dictionary<string, string> { ["NAME/"] = "Source", ["EMPTY"] = "" }));
        typeof(UStringTable).GetProperty(nameof(UStringTable.StringTable))!.SetValue(table, data);

        var entries = EvidenceReader.Read(table);

        Assert.Equal(2, entries.TableEntries.Count);
        Assert.Contains(entries.TableEntries, entry => entry.Pointer == "/StringTable/NAME~1" && entry.Namespace == "Items" && entry.Source == "Source");
        Assert.Contains(entries.TableEntries, entry => entry.Key == "EMPTY" && entry.Source == "");
        Assert.Empty(entries.Issues);
    }

    [Fact]
    public void NativeDataTableRowsUseTypedProperties()
    {
        var table = new TestTable(new() { ["Row/"] = new FStructFallback([Property("QuestId", new Int64Property(42))]) });

        var evidence = EvidenceReader.Read(table);

        Assert.Contains(evidence.Values, value => value.Pointer == "/Rows/Row~1/Properties/0" && value.Value == "42");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void NativeCurveRowsAreEvidenceEvenWithoutCatalogIds()
    {
        var table = new UCurveTable { Name = "Weather", Outer = Root };
        table.RowMap.Add("Wind/Intensity", new FStructFallback([Property("DefaultValue", new FloatProperty(0.75f))]));
        var evidence = EvidenceReader.Read(table);
        Assert.Contains(evidence.Values, value => value.Pointer == "/Rows/Wind~1Intensity/Properties/0" && value.Value == "0.75");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void CompositeCurveEvaluationIsExplicitlyUnresolved()
    {
        var evidence = EvidenceReader.Read(new UCompositeCurveTable { Name = "Composite", Outer = Root });
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Rows" && issue.Message.Contains("parent-table"));
    }

    [Fact]
    public void NativeClassReferencesAreReadWithoutLoadingOrWalkingBytecode()
    {
        var target = new NeverLoadedReference("Default");
        var package = new TestPackage(target);
        var source = new UClass
        {
            Name = "Class",
            Outer = Root,
            ClassDefaultObject = new FPackageIndex(package, 1),
            ClassGeneratedBy = new FPackageIndex(package, 1),
            Children = [new FPackageIndex(package, 1)],
            FuncMap = new() { ["Function"] = new FPackageIndex(package, 1) }
        };

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/ClassDefaultObject" && reference.Role == "class-default" && reference.TargetPath == "/Game/Fixture.Default");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/Children/0" && reference.TargetPath == "/Game/Fixture.Default");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/FuncMap/entries/0/value" && reference.TargetPath == "/Game/Fixture.Default");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void CachedMaterialFieldsKeepExactTextureBindingsWithoutLoadingThem()
    {
        var package = new TestPackage(new NeverLoadedReference("BackgroundMask"));
        var material = new UMaterial
        {
            Name = "Material",
            Outer = Root,
            CachedExpressionData = new FStructFallback([
                Property("ReferencedTextures", new ArrayProperty(new UScriptArray([
                    new ObjectProperty(new FPackageIndex(package, 1)), new ObjectProperty(new FPackageIndex(package, 2))
                ], "ObjectProperty"))),
                Property("ParameterName", new NameProperty("Background"))
            ])
        };

        var evidence = EvidenceReader.Read(material);

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/CachedExpressionData/Properties/0/0"
            && reference.Role == "property" && reference.TargetPath == "/Game/Fixture.BackgroundMask");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/CachedExpressionData/Properties/0/1"
            && reference.Error is not null);
        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/CachedExpressionData/Properties/1" && value.Value == "Background");
        Assert.DoesNotContain(evidence.Values, value => value.Pointer.Contains("LoadedMaterialResources"));
    }

    [Fact]
    public void MergedMaterialTextureHelperDoesNotCreateReferences()
    {
        var texture = new UTexture2D { Name = "Ingredient", Outer = Root };
        texture.Properties.Add(Property("UnrelatedText", new TextProperty(new FText("Texture-owned text"))));
        var material = new UMaterial { Name = "Material", Outer = Root };
        material.ReferencedTextures.Add(texture);
        material.ReferencedTextures.Add(null!);

        var evidence = EvidenceReader.Read(material);

        Assert.DoesNotContain(evidence.References, reference => reference.TargetPath == "/Game/Fixture.Ingredient");
        Assert.DoesNotContain(evidence.References, reference => reference.Pointer.StartsWith("/Native/ReferencedTextures", StringComparison.Ordinal));
        Assert.Empty(evidence.Texts);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void TaggedMaterialTextureBindingsSurviveUpstreamArrayConversion()
    {
        var texture = new UTexture2D { Name = "Bound", Outer = Root };
        var package = new TestPackage(new ResolvedLoadedObject(texture));
        var array = new UScriptArray([
            new ObjectProperty(new FPackageIndex(package, 1)), new ObjectProperty(new FPackageIndex())
        ], "ObjectProperty");
        var tag = Property("ReferencedTextures", new ArrayProperty(array));
        var material = new UMaterial { Name = "Material", Outer = Root };
        material.Properties.Add(tag);

        Assert.True(material.TryGetValue<UTexture[]>(out var converted, "ReferencedTextures"));
        material.ReferencedTextures.AddRange(converted);
        var evidence = EvidenceReader.Read(material);

        Assert.Same(tag, Assert.Single(material.Properties));
        Assert.Same(array, tag.Tag!.GenericValue);
        Assert.Contains(evidence.Properties, property => property.Pointer == "/Properties/0" && property.Name == "ReferencedTextures");
        var binding = Assert.Single(evidence.References, reference => reference.TargetPath == "/Game/Fixture.Bound");
        Assert.Equal("/Properties/0/0", binding.Pointer);
        Assert.Equal("hard", binding.Kind);
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/0/1" && reference.IsNull);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void ReflectedArrayDeclarationsRetainTypesMetadataAndClassReferences()
    {
        var package = new TestPackage(new NeverLoadedReference("DeclaredClass"));
        var source = new UClass
        {
            Name = "Class",
            Outer = Root,
            ChildProperties = [new FArrayProperty
            {
                Name = "Targets", ArrayDim = 1, ElementSize = 16,
                PropertyFlags = EPropertyFlags.BlueprintVisible | EPropertyFlags.Transient,
                Inner = new FObjectProperty { Name = "Targets", PropertyClass = new FPackageIndex(package, 1) }
            }]
        };

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0"
            && value.Kind == "field-declaration" && value.Type == nameof(FArrayProperty));
        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0/Name" && value.Value == "Targets");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0/ArrayDim" && value.Value == "1");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0/PropertyFlags"
            && value.Value!.Contains("BlueprintVisible") && value.Value.Contains("Transient"));
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/ChildProperties/0/Inner/PropertyClass"
            && reference.TargetPath == "/Game/Fixture.DeclaredClass" && reference.PackageIndex == 1);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void FunctionMapDeclarationsRetainEnumKeysAndStructValues()
    {
        var package = new TestPackage(new NeverLoadedReference("DeclaredType"));
        var function = new UFunction
        {
            Name = "Function",
            Outer = Root,
            ChildProperties = [new FMapProperty
            {
                Name = "ByKind",
                KeyProp = new FEnumProperty { Name = "Kind", Enum = new FPackageIndex(package, 1), UnderlyingProp = new FByteProperty() },
                ValueProp = new FStructProperty { Name = "Value", Struct = new FPackageIndex(package, 1) }
            }]
        };

        var evidence = EvidenceReader.Read(function);

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/ChildProperties/0/KeyProp/Enum" && reference.TargetPath == "/Game/Fixture.DeclaredType");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/ChildProperties/0/ValueProp/Struct" && reference.TargetPath == "/Game/Fixture.DeclaredType");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0/KeyProp/UnderlyingProp"
            && value.Kind == "field-declaration" && value.Type == nameof(FByteProperty));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void EmptyDeclarationsAndNullableNestedDeclarationsRemainExplicit()
    {
        var empty = EvidenceReader.Read(new UClass { Name = "Empty", Outer = Root, ChildProperties = [] });
        var optional = EvidenceReader.Read(new UClass
        {
            Name = "Optional",
            Outer = Root,
            ChildProperties = [new FOptionalProperty { Name = "Value", ValueProperty = null }]
        });

        Assert.Contains(empty.Values, value => value.Pointer == "/Native/ChildProperties" && value.Kind == "empty-array");
        Assert.Contains(optional.Values, value => value.Pointer == "/Native/ChildProperties/0/ValueProperty" && value.Kind == "null");
        Assert.Empty(empty.Issues);
        Assert.Empty(optional.Issues);
    }

    [Fact]
    public void CyclicFieldDeclarationsKeepOtherMetadataWithoutRecursiveTraversal()
    {
        var declaration = new FArrayProperty { Name = "Cyclic" };
        declaration.Inner = declaration;

        var evidence = EvidenceReader.Read(new UClass { Name = "Class", Outer = Root, ChildProperties = [declaration] });

        Assert.Contains(evidence.Values, value => value.Pointer == "/Native/ChildProperties/0/Name" && value.Value == "Cyclic");
        Assert.Equal("/Native/ChildProperties/0/Inner", Assert.Single(evidence.Issues).Pointer);
    }

    [Fact]
    public void NativeStructFieldsAreReadableButGettersAndUnknownObjectsAreNot()
    {
        var native = new NativeFields { Label = new FText("Nested"), Unsupported = new object() };

        var evidence = EvidenceReader.Read(Object("Native", Property("Value", new ValueProperty(native))));

        Assert.Equal("Nested", Assert.Single(evidence.Texts).Source);
        Assert.Equal("/Properties/0/Unsupported", Assert.Single(evidence.Issues).Pointer);
    }

    [Fact]
    public void CyclesAndUnreadPropertiesAreDiagnosticsWithoutLosingOtherFields()
    {
        var cycle = new NativeFields { Label = new FText("Still readable") };
        cycle.Unsupported = cycle;
        var source = Object("Cycle", Property("Value", new ValueProperty(cycle)), new FPropertyTag { Name = "Missing", PropertyType = "TextProperty" });

        var evidence = EvidenceReader.Read(source);

        Assert.Equal("Still readable", Assert.Single(evidence.Texts).Source);
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/0/Unsupported" && issue.Message.Contains("cycle"));
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/1");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/1" && value.Kind == "unread");
    }

    [Fact]
    public void NestedObjectIsAnEdgeInsteadOfAbsorbingItsText()
    {
        var child = Object("Child", Property("Label", new TextProperty(new FText("Child text"))));
        var parent = Object("Parent", Property("Object", new ValueProperty(child)));
        child.Outer = new ResolvedLoadedObject(parent);

        var evidence = EvidenceReader.Read(parent);

        Assert.Empty(evidence.Texts);
        Assert.Contains(evidence.References, reference => reference.Kind == "object" && reference.TargetPath == "/Game/Fixture.Parent:Child");
        Assert.Contains(EvidenceReader.Read(child).References, reference => reference.Role == "outer" && reference.TargetPath == "/Game/Fixture.Parent");
    }

    [Fact]
    public void ObjectPathsPreservePackageAndSubobjectSeparators()
    {
        var package = new TestPackage(new NeverLoadedReference("Unused"));
        var asset = Object("Asset");
        asset.Outer = new ResolvedPackageObject(package);
        var child = Object("Child");
        child.Outer = new ResolvedLoadedObject(asset);
        var grandchild = Object("Grandchild");
        grandchild.Outer = new ResolvedLoadedObject(child);

        Assert.Equal("/Game/Fixture.Asset:Child.Grandchild", EvidenceReader.Read(grandchild).Path);
        Assert.Equal(grandchild.GetPathName(), EvidenceReader.Read(grandchild).Path);
        Assert.Contains(EvidenceReader.Read(asset).References, reference => reference.Role == "outer" && reference.TargetPath == "/Game/Fixture");
    }

    [Fact]
    public void OuterCyclesDoNotOverflowTheProcess()
    {
        var first = Object("First");
        var second = Object("Second");
        first.Outer = new ResolvedLoadedObject(second);
        second.Outer = new ResolvedLoadedObject(first);

        var evidence = EvidenceReader.Read(first);

        Assert.Equal("", evidence.Path);
        Assert.Contains(evidence.Issues, issue => issue.Type == "object-path" && issue.Message.Contains("repeats"));
        Assert.Contains(evidence.References, reference => reference.Role == "outer" && reference.Error is not null);
    }

    [Fact]
    public void FreshOuterWrappersCannotCreateAnUnboundedTraversal()
    {
        var source = Object("Loop");
        source.Outer = new FreshOuterReference();

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Issues, issue => issue.Type == "object-path" && issue.Message.Contains("128"));
        Assert.Contains(evidence.References, reference => reference.Role == "outer" && reference.Error!.Contains("128"));
    }

    [Fact]
    public void DisabledSplineHasNoFieldsRatherThanAnUnreadPayload()
    {
        using var archive = new FByteArchive("Disabled spline", [0]);
        var spline = new FSpline(archive);

        var evidence = EvidenceReader.Read(Object("Spline", Property("Spline", new ValueProperty(spline))));

        Assert.Equal(1, archive.Position);
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0" && value.Kind == "empty-struct");
        Assert.Empty(evidence.Issues);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    public void EnabledSplineFailsBeforeEvidenceCanClaimItIsEmpty(byte enabled)
    {
        using var archive = new FByteArchive("Enabled spline", [enabled]);

        Assert.Throws<NotSupportedException>(() => new FSpline(archive));
    }

    [Fact]
    public void RepeatedScalarNamesKeepSeparateOrderedHeadersAndValues()
    {
        var source = Object("Repeated", Property("Value", new IntProperty(1)), Property("Value", new IntProperty(2)));
        source.Class = new ResolvedLoadedObject(new UStruct
        {
            Name = "RuntimeLayout",
            Outer = Root,
            ChildProperties = [new FIntProperty { Name = "Value", ArrayDim = 1 }, new FIntProperty { Name = "Value", ArrayDim = 1 }]
        });

        var evidence = EvidenceReader.Read(source);

        Assert.Equal(["/Properties/0", "/Properties/1"], evidence.Properties.Select(property => property.Pointer));
        Assert.All(evidence.Properties, property =>
        {
            Assert.Equal("Value", property.Name);
            Assert.Equal("IntProperty", property.Type);
            Assert.Null(property.ArrayIndex);
            Assert.Null(property.ArraySize);
            Assert.Equal("Property", property.SerializeType);
        });
        Assert.Equal(["1", "2"], evidence.Values.Select(value => value.Value));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void RepeatedNestedNamesAndArrayPositionsHaveIndependentPropertyContainers()
    {
        var nested = new FStructFallback([Property("0/~", new IntProperty(3)), Property("0/~", new IntProperty(4))]);
        var source = Object("Nested", Property("Array", new ArrayProperty(new UScriptArray([
            new StructProperty(new FScriptStruct(nested))
        ], "StructProperty"))));

        var evidence = EvidenceReader.Read(source);

        Assert.Equal(["/Properties/0", "/Properties/0/0/Properties/0", "/Properties/0/0/Properties/1"],
            evidence.Properties.Select(property => property.Pointer));
        Assert.Equal(["Array", "0/~", "0/~"], evidence.Properties.Select(property => property.Name));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void IndexedTagsRetainElementsAndStillRejectAmbiguousRepeatedElements()
    {
        var first = Property("Array", new IntProperty(1));
        first.PropertyTagFlags = EPropertyTagFlags.HasArrayIndex;
        first.ArraySize = 2;
        var second = Property("Array", new IntProperty(2));
        second.PropertyTagFlags = EPropertyTagFlags.HasArrayIndex;
        second.ArrayIndex = 1;
        second.ArraySize = 2;

        var valid = EvidenceReader.Read(Object("Array", first, second));

        Assert.Equal<int?>([0, 1], valid.Properties.Select(property => property.ArrayIndex));
        Assert.All(valid.Properties, property => Assert.Equal(2, property.ArraySize));
        Assert.Empty(valid.Issues);
        second.Name = "array";
        second.ArrayIndex = 0;
        var invalid = EvidenceReader.Read(Object("Array", first, second));
        Assert.Equal(2, invalid.Values.Count);
        Assert.Contains(invalid.Issues, issue => issue.Pointer == "/Properties/1" && issue.Message.Contains("Repeated indexed"));
    }

    [Fact]
    public void UnreadAndMalformedTagsKeepTheirHeadersAndDiagnostics()
    {
        var skipped = new FPropertyTag { Name = "Unread", PropertyType = "TextProperty", PropertyTagFlags = EPropertyTagFlags.SkippedSerialize };
        var malformed = Property("Array", new IntProperty(1));
        malformed.ArrayIndex = 2;
        malformed.ArraySize = 2;
        var evidence = EvidenceReader.Read(Object("Invalid", skipped, malformed));

        Assert.Equal(2, evidence.Properties.Count);
        Assert.Equal("Skipped", evidence.Properties[0].SerializeType);
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/0" && value.Kind == "unread");
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/0" && issue.Message.Contains("no decoded value"));
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/1" && issue.Message.Contains("Invalid static-array"));
    }

    [Fact]
    public void RuntimeStaticArrayLayoutsAreRejectedThroughActualAncestors()
    {
        var parent = new UStruct { Name = "RuntimeParent", Outer = Root, ChildProperties = [new FIntProperty { Name = "Values", ArrayDim = 2 }] };
        var package = new TestPackage(new ResolvedLoadedObject(parent));
        var child = new UStruct { Name = "RuntimeChild", Outer = Root, ChildProperties = [], SuperStruct = new FPackageIndex(package, 1) };
        var source = Object("Instance", Property("Value", new IntProperty(3)));
        source.Class = new ResolvedLoadedObject(child);
        source.Outer = null; // An absent owner still requires runtime layout validation.
        Assert.Null(source.Owner);

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Issues, issue => issue.Type == "runtime-schema" && issue.Message.Contains("RuntimeParent.Values") && issue.Message.Contains("ArrayDim 2"));
        Assert.Contains(evidence.Values, value => value.Value == "3");
    }

    [Theory]
    [InlineData(EPackageFlags.PKG_None, 0)]
    [InlineData(EPackageFlags.PKG_UnversionedProperties, 1)]
    public void RuntimeArrayGuardMatchesOwningPackageSerialization(EPackageFlags flags, int expectedIssues)
    {
        var runtime = new UStruct { Name = "Runtime", Outer = Root, ChildProperties = [new FIntProperty { Name = "Values", ArrayDim = 2 }] };
        var package = new TestPackage(new ResolvedLoadedObject(runtime), flags);
        var first = Property("Values", new IntProperty(42));
        first.PropertyTagFlags = EPropertyTagFlags.HasArrayIndex;
        first.ArraySize = 2;
        var second = Property("Values", new IntProperty(43));
        second.PropertyTagFlags = EPropertyTagFlags.HasArrayIndex;
        second.ArrayIndex = 1;
        second.ArraySize = 2;
        var source = Object("Instance", first, second);
        source.Class = new ResolvedLoadedObject(runtime);
        source.Outer = new ResolvedPackageObject(package);

        var evidence = EvidenceReader.Read(source);

        Assert.Equal(expectedIssues, evidence.Issues.Count);
        Assert.All(evidence.Issues, issue => Assert.Equal("runtime-schema", issue.Type));
        Assert.Equal(["42", "43"], evidence.Values.Select(value => value.Value));
        Assert.Equal<int?>([0, 1], evidence.Properties.Select(property => property.ArrayIndex));
    }

    [Fact]
    public void RuntimeLayoutGuardStopsAtTheNativeUsmapBoundary()
    {
        var native = new UScriptClass("Native") { Outer = Root, ChildProperties = [new FIntProperty { Name = "Values", ArrayDim = 2 }] };
        var package = new TestPackage(new ResolvedLoadedObject(native));
        var runtime = new UStruct { Name = "Runtime", Outer = Root, ChildProperties = [], SuperStruct = new FPackageIndex(package, 1) };
        var source = Object("Instance");
        source.Class = new ResolvedLoadedObject(runtime);

        Assert.Empty(EvidenceReader.Read(source).Issues);
        source.Class = new ResolvedLoadedObject(native);
        Assert.Empty(EvidenceReader.Read(source).Issues);
    }

    [Fact]
    public void CyclicRuntimeLayoutBecomesADiagnostic()
    {
        var runtime = new UStruct { Name = "Cycle", Outer = Root, ChildProperties = [] };
        runtime.SuperStruct = new FPackageIndex(new TestPackage(new ResolvedLoadedObject(runtime)), 1);
        var source = Object("Instance");
        source.Class = new ResolvedLoadedObject(runtime);

        Assert.Contains(EvidenceReader.Read(source).Issues, issue => issue.Type == "runtime-schema" && issue.Message.Contains("ancestry repeats"));
    }

    private static UObject Object(string name, params FPropertyTag[] properties) => new(properties.ToList()) { Name = name, Outer = Root };
    private static FPropertyTag Property(string name, FPropertyTagType value) => new() { Name = name, PropertyType = value.GetType().Name, Tag = value };
    private sealed class ValueProperty(object? value) : FPropertyTagType
    {
        public override object? GenericValue => value;
        public override string ToString() => "Synthetic typed value";
    }
    private sealed class NativeFields : IUStruct
    {
        public FText Label = new("Default");
        public object? Unsupported;
        public object Computed => throw new InvalidOperationException("Getters must not be evaluated.");
    }
    private sealed class NeverLoadedReference(string name) : ResolvedObject(Root.Package)
    {
        public override FName Name => name;
        public override ResolvedObject Outer => Root;
        public override Lazy<UObject>? Object => throw new InvalidOperationException("Export loading is not evidence reading.");
    }
    private sealed class FreshOuterReference() : ResolvedObject(Root.Package)
    {
        public override FName Name => "Loop";
        public override ResolvedObject Outer => new FreshOuterReference();
    }
    private sealed class TestPackage(ResolvedObject? target, EPackageFlags flags = EPackageFlags.PKG_None) : AbstractUePackage("/Game/Fixture", null)
    {
        public override FPackageFileSummary Summary { get; } = new() { PackageFlags = flags };
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 1;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 } ? target : null;
    }
    private sealed class TestTable : UDataTable
    {
        public TestTable(Dictionary<FName, FStructFallback> rows) { Name = "Table"; Outer = Root; RowMap = rows; RowStructName = "FixtureRow"; }
    }
    private static T Make<T>(params (string Name, object Value)[] fields)
    {
        var value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        foreach (var (name, field) in fields) typeof(T).GetField(name)!.SetValue(value, field);
        return value;
    }
}
