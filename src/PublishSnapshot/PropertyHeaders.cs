using System.Globalization;
using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Only recorded property containers define ordinal slots. A native field named
// Properties does not become a tagged-property container by its spelling.
internal sealed class PropertyHeaders
{
    private readonly Dictionary<string, HashSet<int>> containers = new(StringComparer.Ordinal)
    {
        ["/Properties"] = []
    };

    public static PropertyHeaders Read(JsonElement headers)
    {
        Require(headers.ValueKind == JsonValueKind.Array, "Property headers must be an array.");
        var result = new PropertyHeaders();
        var pointers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var header in headers.EnumerateArray())
        {
            Fields(header, "pointer", "name", "type", "arrayIndex", "arraySize", "serializeType");
            var pointer = String(header, "pointer");
            CheckPointer(pointer);
            var separator = pointer.LastIndexOf('/');
            var container = pointer[..separator];
            Require(container == "/Properties" || container.EndsWith("/Properties", StringComparison.Ordinal),
                "Property header must end in a container's ordinal slot.");
            var ordinal = ReadOrdinal(pointer[(separator + 1)..]);
            Require(pointers.Add(pointer), "Duplicate property header pointer.");
            if (!result.containers.TryGetValue(container, out var slots)) result.containers[container] = slots = [];
            slots.Add(ordinal);
            String(header, "name"); String(header, "type");
            ValidateMetadata(header);
        }
        foreach (var slots in result.containers.Values)
            for (var index = 0; index < slots.Count; index++)
                Require(slots.Contains(index), "Property header ordinals must be contiguous from zero.");
        foreach (var pointer in pointers) result.ValidatePointer(pointer);
        return result;
    }

    public void ValidatePointer(string pointer)
    {
        CheckPointer(pointer);
        for (var separator = pointer.IndexOf('/', 1); separator >= 0; separator = pointer.IndexOf('/', separator + 1))
        {
            if (!containers.TryGetValue(pointer[..separator], out var slots)) continue;
            var next = pointer.IndexOf('/', separator + 1);
            var ordinal = ReadOrdinal(next < 0 ? pointer[(separator + 1)..] : pointer[(separator + 1)..next]);
            Require(slots.Contains(ordinal), $"Field evidence has no property header: {pointer}.");
        }
    }

    private static void ValidateMetadata(JsonElement header)
    {
        var index = NullableInteger(header, "arrayIndex");
        var size = NullableInteger(header, "arraySize");
        Require(index is null or >= 0 && size is null or > 0 && (index is null || size is null || index < size),
            "Invalid property array metadata.");
        Require(String(header, "serializeType") is "Property" or "Skipped" or "BinaryOrNative",
            "Unsupported property serialization type.");
    }

    private static int? NullableInteger(JsonElement header, string field)
    {
        var value = header.GetProperty(field);
        if (value.ValueKind == JsonValueKind.Null) return null;
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), $"Property {field} must be an integer or null.");
        return value.GetInt32();
    }

    private static int ReadOrdinal(string text)
    {
        Require(int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
            ordinal.ToString(CultureInfo.InvariantCulture) == text, "Property pointer ordinal must be a canonical nonnegative integer.");
        return ordinal;
    }

    private static void CheckPointer(string pointer)
    {
        Require(pointer.StartsWith('/'), "Invalid field pointer.");
        for (var index = 0; index < pointer.Length; index++)
            if (pointer[index] == '~')
                Require(++index < pointer.Length && pointer[index] is '0' or '1', "Invalid field pointer escape.");
    }
}
