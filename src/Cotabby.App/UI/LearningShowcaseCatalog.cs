namespace Cotabby.App.UI;

/// <summary>Frozen Mac Home walkthrough order and instructional copy; no provider or user data.</summary>
internal static class LearningShowcaseCatalog
{
    internal sealed record Step(string Title, string Description);
    internal sealed record Scene(string Title, IReadOnlyList<Step> Steps);
    private static Step S(string title, string description) => new(title, description);
    private static readonly Step Preview = S("Translation", "Pause after typing to see a sentence preview.");
    private static readonly Step Expand = S("Expand", "Expand the card to compare the original and translation.");
    private static readonly Step Explain = S("AI explanation", "Use AI to explain the current sentence or expression.");
    private static readonly Step Favorite = S("Save to vocabulary", "Save the current sentence or expression with its context.");
    private static readonly Step FavoriteMenu = S("Saved · Show actions", "Use the saved menu to view or remove this usage.");
    private static readonly Step Speak = S("Read Aloud", "Click the speaker to start reading; click it again to stop.");
    internal static readonly IReadOnlyList<Scene> Scenes = Array.AsReadOnly(new[]
    {
        new Scene("Writing", [Preview, Expand, Explain, Favorite, FavoriteMenu,
            S("Collapse", "Expand the card to compare the original and translation."), Speak,
            S("Replace", "Replace only the preview's saved source range."), S("Dismiss", "Close the preview without changing the text.")]),
        new Scene("Expression assistance", [S("Trigger symbols", "Type your trigger symbols or use the expression shortcut at the caret."),
            S("Describe what you mean", "Describe the expression or term in the temporary input, then pause."),
            S("Select", "Use arrow keys to compare candidates and their hints."), S("Insert", "Press Enter or Tab to insert the selected candidate; Escape cancels.")]),
        new Scene("Reading", [S("Selected text", "Select text outside an editor, then use the translation icon or shortcut."), Preview,
            S("Text under pointer", "Point at a word and use the pointer translation shortcut."), Preview,
            S("Screen region", "Choose a screen region to extract its text and translate it."), Preview, Expand]),
        new Scene("Dictionary", [S("Contextual meaning", "Double-click a word or expression to open its contextual meaning."), Explain,
            S("Dictionary", "Switch from contextual meaning to dictionary definitions."), S("Dictionary sources", "Choose another dictionary source from the source menu."), Favorite, FavoriteMenu]),
        new Scene("Read Aloud", [Preview, Speak, Expand]),
        new Scene("Vocabulary", [S("List", "Expand a headword and select one of its senses."),
            S("Senses", "Click a sense to view its meaning and examples."), S("Graph", "Switch to Graph to browse the same saved expressions."),
            S("Headwords", "Click a headword to reveal its senses; drag nodes to move the graph."), S("Senses", "Click a sense to view its meaning and examples.")]),
        new Scene("Anki", [S("Front", "Choose and arrange content separately for the front and back."),
            S("Back", "Choose and arrange content separately for the front and back."), S("Add to Anki", "Add the configured card to Anki."),
            S("Review in Anki", "Review the saved card in Anki.")]),
    });
}
