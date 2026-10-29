using System.Text;
using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliIdentityTests
{
    [Fact]
    public void SourceOperationSeedKeepsStableUuidVersionAndVariant()
    {
        Assert.Equal(Guid.Parse("0f2a1cd7-dff6-550e-b58e-986e047da655"), CliIdentity.OperationId("task", new string('a', 64), 3, "example.2"));
        Assert.NotEqual(CliIdentity.OperationId("task", new string('a', 64), 3, "example.2"), CliIdentity.OperationId("task", new string('a', 64), 3, "example.3"));
    }
    [Fact]
    public void EquivalentKeyOrderProducesSameFingerprintButChangedInputDoesNot()
    {
        var first = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes("""{"version":1,"idempotencyKey":"k","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"x"}}]}"""));
        var reordered = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes("""{"idempotencyKey":"k","operations":[{"add":{"meaning":"x","kind":"word","language":"en","expression":"bank"}}],"version":1}"""));
        var changed = CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes("""{"version":1,"idempotencyKey":"k","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"y"}}]}"""));
        Assert.Equal(CliIdentity.Fingerprint(first), CliIdentity.Fingerprint(reordered)); Assert.NotEqual(CliIdentity.Fingerprint(first), CliIdentity.Fingerprint(changed));
    }
    [Fact]
    public void TypedOptionalNullIsOmittedButResourcePatchNullRemainsData()
    {
        static CliBatch Parse(string value) => CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(value));
        var omitted = Parse("""{"version":1,"idempotencyKey":"k","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"x"}}]}""");
        var optionalNull = CliInputContract.ValidateWire(System.Text.Json.Nodes.JsonNode.Parse("""{"version":1,"idempotencyKey":"k","expectedRevision":null,"operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"x","examples":null,"note":null}}]}""")!.AsObject());
        Assert.Equal(CliIdentity.Fingerprint(omitted), CliIdentity.Fingerprint(optionalNull));
        Assert.Throws<CliException>(() => Parse("""{"version":1,"idempotencyKey":"k","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"x","examples":null}}]}"""));
        var patchOmitted = Parse("""{"version":2,"idempotencyKey":"k","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"00000000-0000-0000-0000-000000000001"}}}]}""");
        var patchNull = Parse("""{"version":2,"idempotencyKey":"k","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"00000000-0000-0000-0000-000000000001","language":null}}}]}""");
        Assert.NotEqual(CliIdentity.Fingerprint(patchOmitted), CliIdentity.Fingerprint(patchNull));
    }
    [Fact]
    public void TypedUuidCasingDoesNotChangeFingerprintButRawPatchTextDoes()
    {
        static CliBatch Parse(string value) => CliInputContract.DecodeBatch(Encoding.UTF8.GetBytes(value));
        string append = """{"version":1,"idempotencyKey":"k","operations":[{"append":{"entryID":"abcdefab-cdef-abcd-efab-cdefabcdefab","senseID":"abcdefab-cdef-abcd-efab-cdefabcdefac","tags":["x"]}}]}""";
        var a = Parse(append); var b = Parse(append.Replace("abcdefab-cdef-abcd-efab-cdefabcdefab", "ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB"));
        Assert.Equal(CliIdentity.Fingerprint(a), CliIdentity.Fingerprint(b));
        string raw = """{"version":2,"idempotencyKey":"k","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"abcdefab-cdef-abcd-efab-cdefabcdefab"}}}]}""";
        Assert.NotEqual(CliIdentity.Fingerprint(Parse(raw)), CliIdentity.Fingerprint(Parse(raw.Replace("abcdefab-cdef-abcd-efab-cdefabcdefab", "ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB"))));
    }
}
