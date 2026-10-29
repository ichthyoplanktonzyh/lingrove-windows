using Cotabby.Core.CLI;
using Cotabby.Win32.Vocabulary;
using Xunit;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliStoreReadTests
{
    [Fact]
    public async Task ReadsUseTheExistingStoreAndRejectExpiredRequests()
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var now = CliWire.Now();
        var status = await store.ExecuteCliAsync(new CliRequest { Command = "status", ExpiresAt = now + 60 });
        Assert.Equal(0UL, status["revision"]!.GetValue<ulong>());
        Assert.Equal(0, status["entryCount"]!.GetValue<int>());
        var entries = await store.ExecuteCliAsync(new CliRequest { Command = "resourceList", Resource = "entries", ExpiresAt = now + 60 });
        Assert.Equal(0, entries["total"]!.GetValue<int>());
        var error = await Assert.ThrowsAsync<CliException>(() => store.ExecuteCliAsync(new CliRequest { ExpiresAt = now - 1 }));
        Assert.Equal(CliErrorCode.Timeout, error.Code);
        Assert.Equal(0UL, (await store.ReadAsync()).Revision);
    }
}
