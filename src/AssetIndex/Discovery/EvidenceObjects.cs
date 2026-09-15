using System.Text;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal sealed partial class EvidenceReader
{
    private void ReadObject(UObject source)
    {
        ReadResolvedReference(source.Class, "/Class", "class");
        ReadResolvedReference(source.Outer, "/Outer", "outer");
        ReadResolvedReference(source.Super, "/Super", "super");
        ReadResolvedReference(source.Template, "/Template", "template");
        ReadProperties(source.Properties, "/Properties", 0);
        if (source.SerializedSparseClassData is not null) Visit(source.SerializedSparseClassData, "/SparseClassData");
        if (source.CustomGameData is not null) Visit(source.CustomGameData, "/CustomGameData");

        if (source is UDataTable table) ReadDataTable(table);
        if (source is UCurveTable curves) ReadCurveTable(curves);
        if (source is UStringTable strings) ReadStringTable(strings);
        if (source is UField field)
        {
            ReadHardReference(field.SuperField, "/Native/SuperField", "super");
            ReadHardReference(field.Next, "/Native/Next", "property");
        }
        if (source is UStruct structure)
        {
            ReadHardReference(structure.SuperStruct, "/Native/SuperStruct", "super");
            Visit(structure.Children, "/Native/Children");
        }
        if (source is UClass type) ReadClassReferences(type);
    }

    private void ReadClassReferences(UClass type)
    {
        ReadHardReference(type.ClassDefaultObject, "/Native/ClassDefaultObject", "class-default");
        ReadHardReference(type.ClassWithin, "/Native/ClassWithin", "class-within");
        ReadHardReference(type.ClassGeneratedBy, "/Native/ClassGeneratedBy", "class-generated-by");
        Visit(type.FuncMap, "/Native/FuncMap");
        if (type.Interfaces is null) return;
        for (var index = 0; index < type.Interfaces.Length; index++)
        {
            var implemented = type.Interfaces[index];
            ReadHardReference(implemented.Class, $"/Native/Interfaces/{index}/Class", "interface");
            ReadHardReference(implemented.PointerProperty, $"/Native/Interfaces/{index}/PointerProperty", "property");
        }
    }

    private void ReadDataTable(UDataTable table)
    {
        if (table.RowMap is null)
        {
            issues.Add(new("/Rows", nameof(UDataTable), "Data table rows were not decoded."));
            return;
        }
        if (table.RowMap.Count == 0) values.Add(new("/Rows", nameof(UDataTable), "empty-map", null));
        foreach (var (name, row) in table.RowMap.OrderBy(row => row.Key.Text, StringComparer.Ordinal))
            Visit(row, Child("/Rows", name.Text), table.RowStructName);
    }

    private void ReadStringTable(UStringTable table)
    {
        if (table.StringTable?.KeysToEntries is not { } entries)
        {
            issues.Add(new("/StringTable", nameof(UStringTable), "String table entries were not decoded."));
            return;
        }
        if (entries.Count == 0) values.Add(new("/StringTable", nameof(UStringTable), "empty-map", null));
        foreach (var (key, source) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            tableEntries.Add(new(Child("/StringTable", key), table.StringTable.TableNamespace, key, source));
        Visit(table.StringTable.KeysToMetaData, "/StringTableMetadata");
    }

    private void ReadCurveTable(UCurveTable table)
    {
        // Composite rows are derived by loading other exports. Its tagged parent links
        // are retained; do not turn field discovery into implicit row evaluation.
        if (table is UCompositeCurveTable)
        {
            issues.Add(new("/Rows", nameof(UCompositeCurveTable), "Composite curve rows require parent-table evaluation."));
            return;
        }
        values.Add(new("/CurveTableMode", "ECurveTableMode", "enum", table.CurveTableMode.ToString()));
        if (table.RowMap.Count == 0) values.Add(new("/Rows", nameof(UCurveTable), "empty-map", null));
        foreach (var (name, row) in table.RowMap.OrderBy(row => row.Key.Text, StringComparer.Ordinal))
            Visit(row, Child("/Rows", name.Text));
    }

    private void ReadHardReference(FPackageIndex? reference, string pointer, string role)
    {
        if (reference is null || reference.IsNull)
        {
            references.Add(new(pointer, "hard", role, null, true, reference?.Owner?.Name, reference?.Index, null, null));
            return;
        }
        try
        {
            var target = reference.ResolvedObject;
            references.Add(new(pointer, "hard", role, target is null ? null : PathOf(target), false,
                reference.Owner?.Name, reference.Index, target?.ExportIndex,
                target is null ? "Package index has no resolved target." : null));
        }
        catch (Exception error)
        {
            references.Add(new(pointer, "hard", role, null, false, reference.Owner?.Name, reference.Index, null, AssetDiscovery.DescribeError(error)));
        }
    }

    private void ReadResolvedReference(ResolvedObject? reference, string pointer, string role, string kind = "resolved")
    {
        try
        {
            references.Add(new(pointer, kind, role, reference is null ? null : PathOf(reference), reference is null,
                reference?.Package?.Name, null, reference?.ExportIndex, null));
        }
        catch (Exception error)
        {
            references.Add(new(pointer, kind, role, null, false, reference?.Package?.Name, null, reference?.ExportIndex, AssetDiscovery.DescribeError(error)));
        }
    }

    private string? DescribePath(ResolvedObject reference, string pointer)
    {
        try { return PathOf(reference); }
        catch (Exception error)
        {
            issues.Add(new(pointer, "object-path", AssetDiscovery.DescribeError(error)));
            return null;
        }
    }

    private static string PathOf(ResolvedObject reference)
    {
        var chain = new List<ResolvedObject>();
        var seen = new HashSet<ResolvedObject>(ReferenceEqualityComparer.Instance);
        for (var current = reference; current is not null; current = current.Outer)
        {
            if (chain.Count >= 128) throw new InvalidDataException("Object outer chain exceeds 128 levels.");
            if (!seen.Add(current)) throw new InvalidDataException("Object outer chain contains a cycle.");
            chain.Add(current);
        }
        var path = new StringBuilder(chain[^1].Name.Text);
        for (var index = chain.Count - 2; index >= 0; index--)
            path.Append(index == chain.Count - 3 ? ':' : '.').Append(chain[index].Name.Text);
        return path.ToString();
    }
}
