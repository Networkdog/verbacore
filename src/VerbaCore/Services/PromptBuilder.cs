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
                "You are a top-tier linguist and etymology expert helping native Korean speakers internalize the true meanings and nuances of vocabulary. " +
                "Go beyond traditional dictionary definitions by using storytelling, etymology, and native intuition. " +
                "All explanations must be in natural Korean. Use markdown formatting.",

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
        return $$"""
            [Instructions]
            1. Target Language: If the input is English, explain it in Korean. If the input is Korean, explain its English equivalent.
            2. Storytelling Format: For the explanation section, NEVER use numbers or subtitles. Write a natural, engaging prose in 3 paragraphs or less.
            3. Etymology & Visualization: Break down the word's anatomy (roots, prefixes, suffixes) and provide vivid visual imagery for intuitive understanding.
            4. Plain Korean: Translate difficult Hanja-based Korean definitions into easy, everyday conversational Korean.
            5. Native Nuances: Vividly describe the contexts, emotional tones (formal/informal, positive/negative connotations), and idiomatic usages native speakers use.
            6. Synonyms/Antonyms: Provide at least 4 of each. Explicitly explain the subtle nuance differences compared to the main input word.
            7. Pronunciation: Provide the IPA and the closest Korean phonetic spelling.

            [Output Template]
            ## {Input Word} [{IPA}, {Korean Pronunciation}] {Part of Speech}. {Dictionary Definition}

            (Write the storytelling explanation here in Korean based on Instructions 2-5.)

            ### 활용 예시
            * {English Sentence 1} - {Korean Translation} ({Explanation & Nuance})
            * {English Sentence 2} - {Korean Translation} ({Explanation & Nuance})

            [Input Word]: {{word}}
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
