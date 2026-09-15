using System.Runtime.CompilerServices;
using AssetIndex.Discovery;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public void ReadsTypedNestedValuesAndEscapesPropertyPointers()
    {
        var nested = new FStructFallback([
            Property("A/B~C", new Int64Property(long.MinValue)),
            Property("Unsigned", new ValueProperty(ulong.MaxValue)),
            Property("Label", new TextProperty(new FText("Items", "Name", "Actual", "Cached translation")))
        ]);
        var source = Object("Item", Property("Nested", new StructProperty(new FScriptStruct(nested))));

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Nested/A~1B~0C" && value.Type == "Int64Property" && value.Value == "-9223372036854775808");
        Assert.Contains(evidence.Values, value => value.Value == "18446744073709551615" && value.Kind == "integer");
        var text = Assert.Single(evidence.Texts);
        Assert.Equal("/Properties/Nested/Label", text.Pointer);
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

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Data/entries/0/key" && value.Kind == "string");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Data/entries/1/key" && value.Value == "-42");
        Assert.DoesNotContain(evidence.References, reference => reference.TargetPath == "/Game/LooksLikeAPath.Asset");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/Data/entries/0/value" && reference.IsNull);
        Assert.Equal("/Properties/Data/entries/1/value", Assert.Single(evidence.Texts).Pointer);
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

        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Array" && value.Kind == "empty-array");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Set" && value.Kind == "empty-set");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Map" && value.Kind == "empty-map");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Empty" && value.Value == "");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Enabled" && value.Value == "false");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Template" && reference.Role == "template" && reference.TargetPath == "Parent");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Properties/Null" && reference.IsNull);
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

        var hard = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/Hard");
        Assert.Equal("Target", hard.TargetPath);
        Assert.Equal(1, hard.PackageIndex);
        Assert.Null(hard.Error);
        var broken = Assert.Single(evidence.References, reference => reference.Pointer == "/Properties/Broken");
        Assert.False(broken.IsNull);
        Assert.Null(broken.TargetPath);
        Assert.NotNull(broken.Error);
        Assert.Contains(evidence.References, reference => reference.Kind == "resolved" && reference.TargetPath == "Target");
        Assert.Contains(evidence.References, reference => reference.Kind == "soft" && reference.TargetPath == "/Game/A.A:Child");
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
        Assert.Contains(evidence.Texts, text => text.Pointer == "/Properties/Label" && text.History == "NamedFormat");
        Assert.Contains(evidence.Texts, text => text.Pointer == "/Properties/Label/History/SourceFmt" && text.Source == "{item}" && text.Key == "Count");
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
        var table = new UStringTable { Name = "ST_Items" };
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

        Assert.Contains(evidence.Values, value => value.Pointer == "/Rows/Row~1/QuestId" && value.Value == "42");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void NativeClassReferencesAreReadWithoutLoadingOrWalkingBytecode()
    {
        var target = new NeverLoadedReference("Default");
        var package = new TestPackage(target);
        var source = new UClass
        {
            Name = "Class", ClassDefaultObject = new FPackageIndex(package, 1),
            ClassGeneratedBy = new FPackageIndex(package, 1), Children = [new FPackageIndex(package, 1)],
            FuncMap = new() { ["Function"] = new FPackageIndex(package, 1) }
        };

        var evidence = EvidenceReader.Read(source);

        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/ClassDefaultObject" && reference.Role == "class-default" && reference.TargetPath == "Default");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/Children/0" && reference.TargetPath == "Default");
        Assert.Contains(evidence.References, reference => reference.Pointer == "/Native/FuncMap/entries/0/value" && reference.TargetPath == "Default");
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void NativeStructFieldsAreReadableButGettersAndUnknownObjectsAreNot()
    {
        var native = new NativeFields { Label = new FText("Nested"), Unsupported = new object() };

        var evidence = EvidenceReader.Read(Object("Native", Property("Value", new ValueProperty(native))));

        Assert.Equal("Nested", Assert.Single(evidence.Texts).Source);
        Assert.Equal("/Properties/Value/Unsupported", Assert.Single(evidence.Issues).Pointer);
    }

    [Fact]
    public void CyclesAndUnreadPropertiesAreDiagnosticsWithoutLosingOtherFields()
    {
        var cycle = new NativeFields { Label = new FText("Still readable") };
        cycle.Unsupported = cycle;
        var source = Object("Cycle", Property("Value", new ValueProperty(cycle)), new FPropertyTag { Name = "Missing", PropertyType = "TextProperty" });

        var evidence = EvidenceReader.Read(source);

        Assert.Equal("Still readable", Assert.Single(evidence.Texts).Source);
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/Value/Unsupported" && issue.Message.Contains("cycle"));
        Assert.Contains(evidence.Issues, issue => issue.Pointer == "/Properties/Missing");
        Assert.Contains(evidence.Values, value => value.Pointer == "/Properties/Missing" && value.Kind == "unread");
    }

    [Fact]
    public void NestedObjectIsAnEdgeInsteadOfAbsorbingItsText()
    {
        var child = Object("Child", Property("Label", new TextProperty(new FText("Child text"))));
        var parent = Object("Parent", Property("Object", new ValueProperty(child)));
        child.Outer = new ResolvedLoadedObject(parent);

        var evidence = EvidenceReader.Read(parent);

        Assert.Empty(evidence.Texts);
        Assert.Contains(evidence.References, reference => reference.Kind == "object" && reference.TargetPath == "Parent.Child");
        Assert.Contains(EvidenceReader.Read(child).References, reference => reference.Role == "outer" && reference.TargetPath == "Parent");
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
        Assert.Contains(evidence.Issues, issue => issue.Type == "object-path" && issue.Message.Contains("cycle"));
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

    private static UObject Object(string name, params FPropertyTag[] properties) => new(properties.ToList()) { Name = name };
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
    private sealed class NeverLoadedReference(string name) : ResolvedObject(null!)
    {
        public override FName Name => name;
        public override Lazy<UObject>? Object => throw new InvalidOperationException("Export loading is not evidence reading.");
    }
    private sealed class FreshOuterReference() : ResolvedObject(null!)
    {
        public override FName Name => "Loop";
        public override ResolvedObject Outer => new FreshOuterReference();
    }
    private sealed class TestPackage(ResolvedObject target) : AbstractUePackage("/Game/Fixture", null)
    {
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 1;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 } ? target : null;
    }
    private sealed class TestTable : UDataTable
    {
        public TestTable(Dictionary<FName, FStructFallback> rows) { Name = "Table"; RowMap = rows; RowStructName = "FixtureRow"; }
    }
    private static T Make<T>(params (string Name, object Value)[] fields)
    {
        var value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        foreach (var (name, field) in fields) typeof(T).GetField(name)!.SetValue(value, field);
        return value;
    }
}
