using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning.Reading;
namespace Cotabby.Core.Tests.LanguageLearning;
public sealed class ReadingAccessPolicyTests
{
    [Fact] public void SourceGrantsAreExplicitAndIndependentWhileIconInherits()
    {
        PerAppShortcutOverride[] rows = [new() { ProcessName = "editor", SelectionReadingAllowed = true, SelectionIconEnabled = false }];
        Assert.True(ReadingAccessPolicy.Allowed(ReadingSourceKind.Selection, "EDITOR", rows));
        Assert.False(ReadingAccessPolicy.Allowed(ReadingSourceKind.Pointer, "editor", rows));
        Assert.False(ReadingAccessPolicy.Allowed(ReadingSourceKind.Selection, "other", rows));
        Assert.False(ReadingAccessPolicy.Allowed(ReadingSourceKind.Screenshot, null, rows));
        Assert.False(ReadingAccessPolicy.IconAllowed("editor", true, rows));
        Assert.True(ReadingAccessPolicy.IconAllowed("other", true, rows));
        Assert.False(ReadingAccessPolicy.IconAllowed(null, true, rows));
    }
}
