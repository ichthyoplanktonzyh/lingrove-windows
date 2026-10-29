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
}
