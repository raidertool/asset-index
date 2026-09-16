using System.Buffers.Binary;
using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using static AssetIndex.Tests.ArrayEvidenceFixture;

namespace AssetIndex.Tests;

public sealed class NumericArrayEvidenceTests
{
    public static IEnumerable<object[]> NumericCases()
    {
        yield return Case<sbyte>("Int8Property", [-128, -1, 0, 127], value => new Int8Property(value), "80ff007f");
        yield return Case<short>("Int16Property", [short.MinValue, -1, 0, 0x1234, short.MaxValue], value => new Int16Property(value), "0080ffff00003412ff7f");
        yield return Case<ushort>("UInt16Property", [0, 0x1234, ushort.MaxValue], value => new UInt16Property(value), "00003412ffff");
        yield return Case<int>("IntProperty", [int.MinValue, -1, 0, 0x12345678, int.MaxValue], value => new IntProperty(value), "00000080ffffffff0000000078563412ffffff7f");
        yield return Case<uint>("UInt32Property", [0, 0x12345678, uint.MaxValue], value => new UInt32Property(value), "0000000078563412ffffffff");
        yield return Case<long>("Int64Property", [long.MinValue, -1, 0, 0x0102030405060708, long.MaxValue], value => new Int64Property(value),
            "0000000000000080ffffffffffffffff00000000000000000807060504030201ffffffffffffff7f");
        yield return Case<ulong>("UInt64Property", [0, 0x0102030405060708, ulong.MaxValue], value => new UInt64Property(value),
            "00000000000000000807060504030201ffffffffffffffff");
        yield return Case<float>("FloatProperty", [0, -0.0f, BitConverter.Int32BitsToSingle(0x3f800001), float.PositiveInfinity, float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(0x7fc12345), BitConverter.Int32BitsToSingle(unchecked((int)0xffc54321)), float.Epsilon], value => new FloatProperty(value),
            "00000000000000800100803f0000807f000080ff4523c17f2143c5ff01000000");
        yield return Case<double>("DoubleProperty", [0, -0.0, BitConverter.Int64BitsToDouble(0x3ff0000000000001), double.PositiveInfinity, double.NegativeInfinity,
            BitConverter.Int64BitsToDouble(0x7ff8123456789abc), BitConverter.Int64BitsToDouble(unchecked((long)0xfff8fedcba987654)), double.Epsilon], value => new DoubleProperty(value),
            "00000000000000000000000000000080010000000000f03f000000000000f07f000000000000f0ffbc9a78563412f87f547698badcfef8ff0100000000000000");
    }

    [Theory]
    [MemberData(nameof(NumericCases))]
    public void ScriptAndNativeArraysPreserveExactLittleEndianBits(string type, Array native, FPropertyTagType[] tags, string hex)
    {
        var expected = Convert.FromHexString(hex);
        AssertEncoding(Read(new ArrayProperty(new UScriptArray(tags.ToList(), type))), type, expected);
        AssertEncoding(Read(new NativeValue(native)), type, expected);
        AssertEncoding(Read(new ArrayProperty(new UScriptArray([], type))), type, []);
        AssertEncoding(Read(new NativeValue(Array.CreateInstance(native.GetType().GetElementType()!, 0))), type, []);
    }

    [Fact]
    public void LargeUInt16ArrayRetainsEveryDecodedValueInOneRecord()
    {
        const int count = 1_000_003;
        var tags = Enumerable.Range(0, count).Select(index => (FPropertyTagType)new UInt16Property((ushort)index)).ToList();

        var evidence = Read(new ArrayProperty(new UScriptArray(tags, "UInt16Property")));

        var value = Assert.Single(evidence.Values);
        Assert.Equal("numeric-le-base64", value.Kind);
        var bytes = Convert.FromBase64String(value.Value!);
        Assert.Equal(count * 2, bytes.Length);
        for (var index = 0; index < count; index++)
            Assert.Equal((ushort)index, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2)));
        Assert.Empty(evidence.Issues);
    }

    [Theory]
    [InlineData("EChoice", false)]
    [InlineData("EChoice", true)]
    [InlineData("None", false)]
    public void EnumMetadataPreventsNumericCompaction(string name, bool empty)
    {
        var metadata = new FPropertyTagData { EnumName = name };
        if (name == "None") metadata.Enum = new UEnum();
        var array = new UScriptArray(empty ? [] : [new UInt16Property(1)], "UInt16Property", metadata);

        var evidence = Read(new ArrayProperty(array));

        Assert.Equal(empty ? "empty-array" : "integer", Assert.Single(evidence.Values).Kind);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void MismatchedWrappersAndReferencesKeepElementEvidence()
    {
        var array = new UScriptArray([new UInt16Property(12), new Int16Property(-1),
            new NativeValue(new FSoftObjectPath("/Game/Keep.Keep", "Child"))], "UInt16Property");

        var evidence = Read(new ArrayProperty(array));

        Assert.Equal(["12", "-1"], evidence.Values.Select(value => value.Value));
        Assert.All(evidence.Values, value => Assert.Equal("integer", value.Kind));
        var reference = Assert.Single(evidence.References, reference => reference.Role == "property");
        Assert.Equal("/Properties/0/2", reference.Pointer);
        Assert.Equal("/Game/Keep.Keep:Child", reference.TargetPath);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void WrapperSubclassesAndUnknownDeclarationsDoNotQualify()
    {
        var subclass = Read(new ArrayProperty(new UScriptArray([new DerivedUInt16(42)], "UInt16Property")));
        var unknown = Read(new ArrayProperty(new UScriptArray([new UInt16Property(43)], "UnknownProperty")));

        Assert.Equal("integer", Assert.Single(subclass.Values).Kind);
        Assert.Equal("42", Assert.Single(subclass.Values).Value);
        Assert.Equal("integer", Assert.Single(unknown.Values).Kind);
        Assert.Equal("43", Assert.Single(unknown.Values).Value);
    }

    [Fact]
    public void NonnumericNativeArraysAndBoxedNumbersKeepElementEvidence()
    {
        Assert.Equal("enum", Assert.Single(Read(new NativeValue(new Choice[] { Choice.First })).Values).Kind);
        Assert.Equal("enum", Assert.Single(Read(new NativeValue(new ByteChoice[] { ByteChoice.First })).Values).Kind);
        Assert.Equal("boolean", Assert.Single(Read(new NativeValue(new[] { true })).Values).Kind);
        Assert.Equal("string", Assert.Single(Read(new NativeValue(new[] { "text" })).Values).Kind);
        Assert.Equal("integer", Assert.Single(Read(new NativeValue(new object[] { 12 })).Values).Kind);
    }

    private static object[] Case<T>(string type, T[] values, Func<T, FPropertyTagType> wrap, string hex) =>
        [type, values, values.Select(wrap).ToArray(), hex];

    private static void AssertEncoding(ObjectEvidence evidence, string type, byte[] expected)
    {
        var value = Assert.Single(evidence.Values);
        Assert.Equal("/Properties/0", value.Pointer);
        Assert.Equal(type + "[]", value.Type);
        Assert.Equal("numeric-le-base64", value.Kind);
        Assert.Equal(expected, Convert.FromBase64String(value.Value!));
        Assert.DoesNotContain(evidence.References, reference => reference.Role == "property");
        Assert.Empty(evidence.Issues);
    }

    private sealed class DerivedUInt16(ushort value) : UInt16Property(value);
    private enum Choice { First }
    private enum ByteChoice : byte { First }
}
