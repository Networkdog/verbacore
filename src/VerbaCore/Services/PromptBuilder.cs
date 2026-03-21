using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class PromptBuilder
{
    public string Build(string input, LookupMode mode, string sourceLanguage, string targetLanguage)
    {
        return mode switch
        {
            LookupMode.Dictionary => BuildDictionaryPrompt(input, sourceLanguage, targetLanguage),
            LookupMode.Translate => BuildTranslatePrompt(input, sourceLanguage, targetLanguage),
            LookupMode.Analyze => BuildAnalyzePrompt(input, sourceLanguage, targetLanguage),
            _ => BuildDictionaryPrompt(input, sourceLanguage, targetLanguage)
        };
    }

    public string GetSystemMessage(LookupMode mode)
    {
        return mode switch
        {
            LookupMode.Dictionary =>
                "You are a professional multilingual dictionary assistant. " +
                "Provide clear, well-structured definitions with pronunciation, examples, and related words. " +
                "Use markdown formatting for readability.",

            LookupMode.Translate =>
                "You are a professional translator. " +
                "Provide accurate translations with brief notes on nuances when applicable. " +
                "Use markdown formatting for readability.",

            LookupMode.Analyze =>
                "You are a linguistic analysis expert. " +
                "Provide detailed grammatical breakdowns with clear explanations. " +
                "Use markdown formatting for readability.",

            _ => "You are a helpful language assistant."
        };
    }

    private static string BuildDictionaryPrompt(string word, string sourceLanguage, string targetLanguage)
    {
        return $"""
            Define the word or phrase: "{word}"

            Please include:
            - **Pronunciation** (IPA notation)
            - **Part of speech**
            - **Definitions** with example sentences (numbered)
            - **Synonyms** and **Antonyms**
            - **Etymology** (brief)

            The word is in {sourceLanguage}. Respond in {targetLanguage}.
            """;
    }

    private static string BuildTranslatePrompt(string text, string sourceLanguage, string targetLanguage)
    {
        return $"""
            Translate the following text from {sourceLanguage} to {targetLanguage}:

            "{text}"

            Provide:
            1. The **translation**
            2. Brief **notes** on nuances or alternative translations (if applicable)
            """;
    }

    private static string BuildAnalyzePrompt(string text, string sourceLanguage, string targetLanguage)
    {
        return $"""
            Analyze the following {sourceLanguage} text grammatically:

            "{text}"

            Break down:
            - **Sentence structure** (subject, verb, object, etc.)
            - **Parts of speech** for each key word
            - **Tense** and **voice**
            - **Idiomatic expressions** (if any)
            - **Meaning summary**

            Explain in {targetLanguage}.
            """;
    }
}
