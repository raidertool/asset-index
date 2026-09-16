using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;

namespace AssetIndex.Discovery;

internal static class NumericArrayEvidence
{
    public static ValueEvidence? Read(UScriptArray array, string pointer)
    {
        var enumName = array.InnerTagData?.EnumName;
        if (array.InnerTagData?.Enum is not null ||
            enumName is not null && !enumName.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        return array.InnerType switch
        {
            "Int8Property" => Read<Int8Property, sbyte>(array, pointer),
            "Int16Property" => Read<Int16Property, short>(array, pointer),
            "UInt16Property" => Read<UInt16Property, ushort>(array, pointer),
            "IntProperty" => Read<IntProperty, int>(array, pointer),
            "UInt32Property" => Read<UInt32Property, uint>(array, pointer),
            "Int64Property" => Read<Int64Property, long>(array, pointer),
            "UInt64Property" => Read<UInt64Property, ulong>(array, pointer),
            "FloatProperty" => Read<FloatProperty, float>(array, pointer),
            "DoubleProperty" => Read<DoubleProperty, double>(array, pointer),
            _ => null
        };
    }

    public static ValueEvidence? Read(Array array, string pointer)
    {
        // CLR array casts can accept enums or the opposite integer signedness.
        var type = array.GetType();
        return array switch
        {
            sbyte[] items when type == typeof(sbyte[]) => Read<sbyte>(items, pointer, "Int8Property"),
            short[] items when type == typeof(short[]) => Read<short>(items, pointer, "Int16Property"),
            ushort[] items when type == typeof(ushort[]) => Read<ushort>(items, pointer, "UInt16Property"),
            int[] items when type == typeof(int[]) => Read<int>(items, pointer, "IntProperty"),
            uint[] items when type == typeof(uint[]) => Read<uint>(items, pointer, "UInt32Property"),
            long[] items when type == typeof(long[]) => Read<long>(items, pointer, "Int64Property"),
            ulong[] items when type == typeof(ulong[]) => Read<ulong>(items, pointer, "UInt64Property"),
            float[] items when type == typeof(float[]) => Read<float>(items, pointer, "FloatProperty"),
            double[] items when type == typeof(double[]) => Read<double>(items, pointer, "DoubleProperty"),
            _ => null
        };
    }

    private static ValueEvidence? Read<TProperty, T>(UScriptArray array, string pointer)
        where TProperty : FPropertyTagType<T> where T : unmanaged
    {
        if (array.Properties.Any(property => property?.GetType() != typeof(TProperty))) return null;
        var bytes = new byte[checked(array.Properties.Count * Unsafe.SizeOf<T>())];
        var items = MemoryMarshal.Cast<byte, T>(bytes.AsSpan());
        for (var index = 0; index < items.Length; index++)
            items[index] = ((TProperty)array.Properties[index]).Value;
        return Encode(bytes, Unsafe.SizeOf<T>(), pointer, array.InnerType);
    }

    private static ValueEvidence Read<T>(ReadOnlySpan<T> items, string pointer, string type) where T : unmanaged =>
        Encode(MemoryMarshal.AsBytes(items).ToArray(), Unsafe.SizeOf<T>(), pointer, type);

    private static ValueEvidence Encode(byte[] bytes, int width, string pointer, string type)
    {
        // Copy primitive bits without numeric conversion, preserving NaN payloads and signed zero.
        if (!BitConverter.IsLittleEndian)
            for (var offset = 0; offset < bytes.Length; offset += width)
                bytes.AsSpan(offset, width).Reverse();
        return new(pointer, type + "[]", "numeric-le-base64", Convert.ToBase64String(bytes));
    }
}
