using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using static AssetIndex.Tests.ArrayEvidenceFixture;

namespace AssetIndex.Tests;

public sealed class ByteEvidenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public void PlainByteArraysRetainEveryByteInOneValue(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
        var array = new UScriptArray(bytes.Select(value => (FPropertyTagType)new ByteProperty(value)).ToList(), "ByteProperty");

        var evidence = Read(new ArrayProperty(array));

        var value = Assert.Single(evidence.Values);
        Assert.Equal("/Properties/0", value.Pointer);
        Assert.Equal("ByteProperty[]", value.Type);
        Assert.Equal("binary-base64", value.Kind);
        Assert.Equal(bytes, Convert.FromBase64String(value.Value!));
        Assert.DoesNotContain(evidence.References, reference => reference.Role == "property");
        Assert.Empty(evidence.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBytesUseTheSameLosslessEncoding(bool empty)
    {
        byte[] bytes = empty ? [] : [0, 255, 128, 42];

        var evidence = Read(new NativeValue(bytes));

        var value = Assert.Single(evidence.Values);
        Assert.Equal("binary-base64", value.Kind);
        Assert.Equal(bytes, Convert.FromBase64String(value.Value!));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void ByteEnumElementsRemainNamedValuesEvenWithoutDeclarationMetadata()
    {
        var evidence = Read(new ArrayProperty(new UScriptArray(
            [new EnumProperty(new FName("EChoice::First")), new EnumProperty(new FName("EChoice::Second"))], "ByteProperty")));

        Assert.Collection(evidence.Values,
            value => { Assert.Equal("/Properties/0/0", value.Pointer); Assert.Equal("EChoice::First", value.Value); },
            value => { Assert.Equal("/Properties/0/1", value.Pointer); Assert.Equal("EChoice::Second", value.Value); });
        Assert.All(evidence.Values, value => Assert.Equal("name", value.Kind));
        Assert.Empty(evidence.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumDeclarationsPreventCompactionEvenForByteElements(bool empty)
    {
        var array = new UScriptArray(empty ? [] : [new ByteProperty(1)], "ByteProperty", new() { EnumName = "EChoice" });

        var evidence = Read(new ArrayProperty(array));

        Assert.Equal(empty ? "empty-array" : "integer", Assert.Single(evidence.Values).Kind);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void MixedByteArraysKeepTheirReferenceEdgesAndScalarValues()
    {
        var array = new UScriptArray([new ByteProperty(1), new NativeValue(new FSoftObjectPath("/Game/Keep.Keep", "Child"))], "ByteProperty");

        var evidence = Read(new ArrayProperty(array));

        var value = Assert.Single(evidence.Values);
        Assert.Equal("/Properties/0/0", value.Pointer);
        Assert.Equal("integer", value.Kind);
        var reference = Assert.Single(evidence.References, reference => reference.Role == "property");
        Assert.Equal("/Properties/0/1", reference.Pointer);
        Assert.Equal("/Game/Keep.Keep:Child", reference.TargetPath);
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void ReferenceArraysRemainElementEvidence()
    {
        var references = Read(new ArrayProperty(new UScriptArray([new ObjectProperty(new FPackageIndex())], "ObjectProperty")));

        Assert.Equal("/Properties/0/0", Assert.Single(references.References, reference => reference.Role == "property").Pointer);
        Assert.Empty(references.Values);
        Assert.Empty(references.Issues);
    }

}
