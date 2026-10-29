namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Fixed trusted GBNF from LearningOutputGrammar.swift; never interpolates user text.</summary>
public static class ReadingLookupGrammar
{
    public static string ForContext(bool hasContext) => $$"""
        root ::= "{" ws "\"meaning\"" ws ":" ws string ws "," ws "\"sentence_translation\"" ws ":" ws {{(hasContext ? "string" : "empty")}} ws "}" ws
        string ::= "\"" char+ "\""
        empty ::= "\"\""
        char ::= [^"\\\x00-\x1F\x7F] | "\\" (["\\/bfnrt] | "u" [0-9a-fA-F]{4})
        ws ::= [ \t\n\r]{0,16}
        """.Replace("\r\n", "\n", StringComparison.Ordinal);
}
