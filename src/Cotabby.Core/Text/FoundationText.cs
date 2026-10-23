using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Text;

/// <summary>Shared Foundation-compatible string projection for UI adapters. Presentation can
/// borrow the already-audited trim rule without exposing the learning subsystem's internal text
/// machinery or substituting .NET's different whitespace set. This stateless boundary owns no
/// settings or capture; it returns a new value and leaves the caller's retained text untouched.</summary>
public static class FoundationText
{
    public static string TrimWhitespaceAndNewlines(string text) => LearningExplanationText.Trim(text);
}
