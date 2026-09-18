using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Assets.Exports.Material;
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
        CheckRuntimeLayout(source);
        ReadProperties(source.Properties, "/Properties", 0);
        if (source.SerializedSparseClassData is not null) Visit(source.SerializedSparseClassData, "/SparseClassData");
        if (source.CustomGameData is not null) Visit(source.CustomGameData, "/CustomGameData");

        if (source is UDataTable table) ReadDataTable(table);
        if (source is UCurveTable curves) ReadCurveTable(curves);
        if (source is UStringTable strings) ReadStringTable(strings);
        if (source is UMaterialInterface material)
            Visit(material.CachedExpressionData, "/Native/CachedExpressionData");
        if (source is UField field)
        {
            ReadHardReference(field.SuperField, "/Native/SuperField", "super");
            ReadHardReference(field.Next, "/Native/Next", "property");
        }
        if (source is UStruct structure)
        {
            ReadHardReference(structure.SuperStruct, "/Native/SuperStruct", "super");
            Visit(structure.Children, "/Native/Children");
            Visit(structure.ChildProperties, "/Native/ChildProperties");
        }
        if (source is UClass type) ReadClassReferences(type);
    }

    private void CheckRuntimeLayout(UObject source)
    {
        try
        {
            var current = source.Class?.Object?.Value as UStruct;
            if (current is null or UScriptClass) return;
            // Tagged properties carry their own layout and do not use SerializedStruct.
            if (source.Owner is { } owner && !owner.HasFlags(EPackageFlags.PKG_UnversionedProperties)) return;
            var seen = new HashSet<UStruct>(ReferenceEqualityComparer.Instance);
            // The pinned CUE runtime declaration mapper cannot safely expand static arrays.
            // Native UScriptClass layouts use usmap's separate, correct array expansion.
            while (current is not null && current is not UScriptClass)
            {
                if (!seen.Add(current) || seen.Count > 128)
                    throw new InvalidDataException("Runtime class ancestry repeats or exceeds 128 levels.");
                foreach (var field in current.ChildProperties ?? [])
                    if (field is FProperty { ArrayDim: > 1 } property)
                        issues.Add(new("/Class", "runtime-schema",
                            $"Runtime declaration {current.GetPathName()}.{property.Name.Text} has ArrayDim {property.ArrayDim}; the pinned decoder cannot safely map it."));
                if (current.SuperStruct is null or { IsNull: true }) break;
                current = current.SuperStruct.Load<UStruct>()
                    ?? throw new InvalidDataException("Runtime class parent could not be loaded.");
            }
        }
        catch (Exception error) { issues.Add(new("/Class", "runtime-schema", AssetDiscovery.DescribeError(error))); }
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
            if (reference.Owner?.Provider is PackageProvider provider && provider.InputIndex.MissingHard(reference) is { } missing)
            {
                references.Add(new(pointer, "hard", role, null, false, reference.Owner.Name, reference.Index, null,
                    "Referenced package is unavailable in the supplied inputs.")
                { Unavailable = missing });
                return;
            }
            var target = reference.ResolvedObject;
            references.Add(new(pointer, "hard", role, target is null ? null : ObjectMetadata.Path(target), false,
                reference.Owner?.Name, reference.Index, target?.ExportIndex,
                target is null ? ImportDiagnostics.Describe(reference, "Package index has no resolved target.") : null));
        }
        catch (Exception error)
        {
            references.Add(new(pointer, "hard", role, null, false, reference.Owner?.Name, reference.Index, null,
                ImportDiagnostics.Describe(reference, AssetDiscovery.DescribeError(error))));
        }
    }

    private void ReadResolvedReference(ResolvedObject? reference, string pointer, string role, string kind = "resolved")
    {
        try
        {
            references.Add(new(pointer, kind, role, reference is null ? null : ObjectMetadata.Path(reference), reference is null,
                reference?.Package?.Name, null, reference?.ExportIndex, null));
        }
        catch (Exception error)
        {
            references.Add(new(pointer, kind, role, null, false, reference?.Package?.Name, null, reference?.ExportIndex, AssetDiscovery.DescribeError(error)));
        }
    }

    private string? DescribePath(ResolvedObject reference, string pointer)
    {
        try { return ObjectMetadata.Path(reference); }
        catch (Exception error)
        {
            issues.Add(new(pointer, "object-path", AssetDiscovery.DescribeError(error)));
            return null;
        }
    }
}
