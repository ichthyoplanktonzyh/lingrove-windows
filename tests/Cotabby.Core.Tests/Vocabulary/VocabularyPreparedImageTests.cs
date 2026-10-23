using Cotabby.Core.Vocabulary;
namespace Cotabby.Core.Tests.Vocabulary;
public sealed class VocabularyPreparedImageTests
{
    [Fact]
    public void DraftBytesAreDetachedFromBothTheCallerAndReturnedCopies()
    {
        byte[] input = [1, 2, 3]; var reference = new VocabularyImageReference(new string('a', 64), 1, 1, 3);
        var value = new VocabularyPreparedImage(reference, input); input[0] = 9; var returned = value.CopyBytes(); returned[1] = 8;
        Assert.Equal(new byte[] { 1, 2, 3 }, value.CopyBytes()); Assert.Same(reference, value.Reference);
    }
}
