using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal sealed partial class EvidenceReader
{
    // Native adapters cover tables, declarations, class links and material fields. Shader, mesh, audio and other
    // opaque export payloads are outside field evidence; their resource still exists.
    public const string NativeScope = "Tagged and sparse properties; data/curve/string tables; class references and field declarations; cached material fields and serialized texture bindings. Binary payloads and composite curve evaluation are not decoded.";
    private readonly List<PropertyEvidence> properties = [];
    private readonly List<ReferenceEvidence> references = [];
    private readonly List<TextEvidence> texts = [];
    private readonly List<ValueEvidence> values = [];
    private readonly List<TableEntryEvidence> tableEntries = [];
    private readonly List<EvidenceIssue> issues = [];
    private readonly HashSet<object> active = new(ReferenceEqualityComparer.Instance);

    private EvidenceReader() { }

    public static ObjectEvidence Read(UObject source)
    {
        var reader = new EvidenceReader();
        var path = reader.DescribePath(new ResolvedLoadedObject(source), "");
        reader.ReadObject(source);
        return new(path ?? "", source.ExportType, reader.properties.ToArray(), reader.references.ToArray(), reader.texts.ToArray(),
            reader.values.ToArray(), reader.tableEntries.ToArray(), reader.issues.ToArray());
    }

    private void ReadProperties(IEnumerable<FPropertyTag> tags, string pointer, int depth, string containerType = "properties")
    {
        var indexed = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var property in tags)
        {
            var path = Child(pointer, (count++).ToString(CultureInfo.InvariantCulture));
            var type = property.PropertyType.IsNone ? property.Tag?.GetType().Name ?? "UnknownProperty" : property.PropertyType.Text;
            properties.Add(new(path, property.Name.Text, type, property.IsIndexed ? property.ArrayIndex : null,
                property.ArraySize, property.SerializeType.ToString()));
            if (property.ArrayIndex < 0 || property.ArraySize is <= 0 ||
                property.ArraySize is { } size && property.ArrayIndex >= size)
                issues.Add(new(path, type, "Invalid static-array tag metadata."));
            if (property.IsIndexed)
            {
                if (!indexed.TryGetValue(property.Name.Text, out var indices)) indexed[property.Name.Text] = indices = [];
                if (!indices.Add(property.ArrayIndex))
                    issues.Add(new(path, type, "Repeated indexed property element has no distinct declaration identity."));
            }
            if (property.Tag is null)
            {
                values.Add(new(path, type, "unread", null));
                issues.Add(new(path, type, $"Property has no decoded value ({property.SerializeType})."));
                continue;
            }
            Visit(property.Tag, path, type, depth + 1);
        }
        if (count == 0) values.Add(new(pointer, containerType, "empty-struct", null));
    }

    private void Visit(object? value, string pointer, string? type = null, int depth = 0)
    {
        type ??= value?.GetType().Name ?? "unknown";
        if (depth > 128)
        {
            issues.Add(new(pointer, type, "Field nesting exceeds 128 levels."));
            return;
        }
        if (value is not null && !value.GetType().IsValueType && !active.Add(value))
        {
            issues.Add(new(pointer, type, "Compound value contains a cycle."));
            return;
        }
        try { ReadValue(value, pointer, type, depth); }
        catch (Exception error) { issues.Add(new(pointer, type, AssetDiscovery.DescribeError(error))); }
        finally { if (value is not null && !value.GetType().IsValueType) active.Remove(value); }
    }

    private void ReadValue(object? value, string pointer, string type, int depth)
    {
        switch (value)
        {
            case null: values.Add(new(pointer, type, "null", null)); return;
            case FPropertyTagType property: Visit(property.GenericValue, pointer, type, depth + 1); return;
            case FScriptStruct structure: Visit(structure.StructType, pointer, type, depth + 1); return;
            case FStructFallback structure: ReadProperties(structure.Properties, Child(pointer, "Properties"), depth, type); return;
            case UScriptArray array: ReadArray(array, pointer, type, depth); return;
            case UScriptSet set: ReadSequence(set.Properties, pointer, type, "set", depth); return;
            case UScriptMap map: ReadMap(map.Properties, pointer, type, depth); return;
            case FPackageIndex reference: ReadHardReference(reference, pointer, "property"); return;
            case FSoftObjectPath reference:
                references.Add(new(pointer, "soft", "property", reference.AssetPathName.IsNone ? null : reference.ToString(),
                    reference.AssetPathName.IsNone, reference.Owner?.Name, null, null, null));
                return;
            case ResolvedObject reference: ReadResolvedReference(reference, pointer, "property"); return;
            case UObject export: ReadResolvedReference(new ResolvedLoadedObject(export), pointer, "property", "object"); return;
            case FText text: ReadText(text, pointer, depth); return;
            case FName name: values.Add(new(pointer, type, "name", name.Text)); return;
            case string text: values.Add(new(pointer, type, "string", text)); return;
            case bool boolean: values.Add(new(pointer, type, "boolean", boolean ? "true" : "false")); return;
            case char character: values.Add(new(pointer, type, "character", character.ToString())); return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or BigInteger:
                values.Add(new(pointer, type, "integer", ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)));
                return;
            case float single: values.Add(new(pointer, type, "float", single.ToString("R", CultureInfo.InvariantCulture))); return;
            case double number: values.Add(new(pointer, type, "float", number.ToString("R", CultureInfo.InvariantCulture))); return;
            case decimal number: values.Add(new(pointer, type, "decimal", number.ToString(CultureInfo.InvariantCulture))); return;
            case Enum enumeration: values.Add(new(pointer, type, "enum", enumeration.ToString())); return;
            case byte[] bytes when value.GetType() == typeof(byte[]): ReadBytes(bytes, pointer, type); return;
            case Array array when NumericArrayEvidence.Read(array, pointer) is { } numeric:
                values.Add(numeric);
                return;
            case IDictionary map: ReadMap(map, pointer, type, depth); return;
            case IList sequence: ReadSequence(sequence, pointer, type, "array", depth); return;
            case FField field:
                values.Add(new(pointer, field.GetType().Name, "field-declaration", null));
                ReadNativeFields(field, pointer, depth);
                return;
            case FScriptDelegate or FMulticastScriptDelegate or FFieldPath or FScriptInterface or FUniqueObjectGuid:
                ReadNativeFields(value, pointer, depth);
                return;
            // Upstream FSpline accepts only the disabled, zero-payload form;
            // enabled forms throw during deserialization before reaching us.
            case FSpline: values.Add(new(pointer, type, "empty-struct", null)); return;
            case IUStruct structure: ReadNativeFields(structure, pointer, depth); return;
            default: issues.Add(new(pointer, type, $"Unsupported compound value: {value.GetType().FullName}.")); return;
        }
    }

    private void ReadArray(UScriptArray array, string pointer, string type, int depth)
    {
        if (NumericArrayEvidence.Read(array, pointer) is { } numeric)
        {
            values.Add(numeric);
            return;
        }
        // ByteProperty arrays can decode as enum names. Compact only proven plain
        // bytes; other element types still expose their values and references.
        var enumName = array.InnerTagData?.EnumName;
        if (array.InnerType != "ByteProperty" || array.InnerTagData?.Enum is not null ||
            enumName is not null && !enumName.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            array.Properties.Any(property => property is not ByteProperty))
        {
            ReadSequence(array.Properties, pointer, type, "array", depth);
            return;
        }
        var bytes = new byte[array.Properties.Count];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = ((ByteProperty)array.Properties[index]).Value;
        ReadBytes(bytes, pointer, array.InnerType + "[]");
    }

    private void ReadBytes(byte[] bytes, string pointer, string type) =>
        values.Add(new(pointer, type, "binary-base64", Convert.ToBase64String(bytes)));

    private void ReadSequence(IList sequence, string pointer, string type, string kind, int depth)
    {
        if (sequence.Count == 0) values.Add(new(pointer, type, "empty-" + kind, null));
        for (var index = 0; index < sequence.Count; index++)
            Visit(sequence[index], Child(pointer, index.ToString(CultureInfo.InvariantCulture)), depth: depth + 1);
    }

    private void ReadMap(IDictionary map, string pointer, string type, int depth)
    {
        if (map.Count == 0) values.Add(new(pointer, type, "empty-map", null));
        var index = 0;
        foreach (DictionaryEntry entry in map)
        {
            var path = Child(Child(pointer, "entries"), (index++).ToString(CultureInfo.InvariantCulture));
            Visit(entry.Key, Child(path, "key"), depth: depth + 1);
            Visit(entry.Value, Child(path, "value"), depth: depth + 1);
        }
    }

    // The visitor calls this only for CUE's typed structs and reflected FField
    // declarations and explicit property wrappers. UObject values stay reference edges;
    // getters are never evaluated.
    private void ReadNativeFields(object value, string pointer, int depth)
    {
        var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
        if (fields.Length == 0)
            issues.Add(new(pointer, value.GetType().Name, "Native struct exposes no readable fields."));
        foreach (var field in fields)
            Visit(field.GetValue(value), Child(pointer, field.Name), field.FieldType.Name, depth + 1);
    }

    private void ReadText(FText text, string pointer, int depth)
    {
        var history = text.TextHistory;
        var entry = history switch
        {
            FTextHistory.Base source => new TextEvidence(pointer, (uint)text.Flags, text.HistoryType.ToString(), source.Namespace, source.Key, source.SourceString, null),
            FTextHistory.None source => new(pointer, (uint)text.Flags, text.HistoryType.ToString(), null, null, source.CultureInvariantString, null),
            FTextHistory.StringTableEntry source => new(pointer, (uint)text.Flags, text.HistoryType.ToString(), null, source.Key, null, source.TableId.Text),
            _ => new(pointer, (uint)text.Flags, text.HistoryType.ToString(), null, null, null, null)
        };
        texts.Add(entry);
        // String-table SourceString is also cached globally; only the table adapter
        // supplies its source. Never consume the cached Text/LocalizedString getters.
        // Composite histories expose typed source and arguments, including nested FText.
        if (history is not (FTextHistory.Base or FTextHistory.None or FTextHistory.StringTableEntry))
            Visit(history, Child(pointer, "History"), depth: depth + 1);
    }

    private static string Child(string pointer, string name) => pointer + "/" + name.Replace("~", "~0").Replace("/", "~1");
}
