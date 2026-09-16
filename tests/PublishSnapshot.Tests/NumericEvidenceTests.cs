namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("Int8Property[]", "807f")]
    [InlineData("Int16Property[]", "0080ff7f")]
    [InlineData("UInt16Property[]", "0000ffff")]
    [InlineData("IntProperty[]", "00000080ffffff7f")]
    [InlineData("UInt32Property[]", "00000000ffffffff")]
    [InlineData("Int64Property[]", "0000000000000080ffffffffffffff7f")]
    [InlineData("UInt64Property[]", "0000000000000000ffffffffffffffff")]
    [InlineData("FloatProperty[]", "000000807856c07f")]
    [InlineData("DoubleProperty[]", "0000000000000080785600000000f87f")]
    [InlineData("FloatProperty[]", "")]
    public void NumericEvidencePreservesTypedPayloadAndIsRetryable(string type, string hex)
    {
        AddEncodedEvidence(Convert.ToBase64String(Convert.FromHexString(hex)), type, "numeric-le-base64");
        AssertEvidencePublishesUnchanged();
    }

    [Theory]
    [InlineData("UInt16Property[]", "AA==")]
    [InlineData("IntProperty[]", "AAA=")]
    [InlineData("UInt64Property[]", "AAAAAA==")]
    [InlineData("FloatProperty[]", "AAAAAAA=")]
    [InlineData("DoubleProperty[]", "AAAAAAAAAAAA")]
    [InlineData("FloatProperty[]", null)]
    [InlineData("Int8Property[]", "AB==")]
    [InlineData("Int8Property[]", "AA==\n")]
    [InlineData("UInt16Property[]", "!")]
    [InlineData("ByteProperty[]", "AA==")]
    [InlineData("BoolProperty[]", "AA==")]
    [InlineData("ObjectProperty[]", "AAAAAA==")]
    [InlineData("uint16property[]", "AAA=")]
    [InlineData("Unknown[]", "")]
    public void InvalidNumericEvidenceCannotReachGitOrChangePublishedReferences(string type, string? encoded)
    {
        AddEncodedEvidence(encoded, type, "numeric-le-base64");
        AssertEvidenceRejectedBeforeGit();
    }
}
