using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class PromptBuilder
{
    /// <summary>
    /// Auto-selects Dictionary for short input (≤3 words), Translate for longer input.
    /// </summary>
    public static LookupMode AutoSelectMode(string input)
    {
        var wordCount = input.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount <= 3 ? LookupMode.Dictionary : LookupMode.Translate;
    }

    public string Build(string input, LookupMode mode, string sourceLanguage, string targetLanguage)
    {
        return mode switch
        {
            LookupMode.Dictionary => BuildDictionaryPrompt(input, sourceLanguage, targetLanguage),
            LookupMode.Translate => BuildTranslatePrompt(input, sourceLanguage, targetLanguage),
            _ => BuildDictionaryPrompt(input, sourceLanguage, targetLanguage)
        };
    }

    public string GetSystemMessage(LookupMode mode)
    {
        return mode switch
        {
            LookupMode.Dictionary =>
                "You are a top-tier linguist and etymology expert helping native Korean speakers deeply understand English vocabulary. " +
                "You explain ENGLISH words — keep the English word as-is in the heading, provide IPA for the ENGLISH pronunciation, and write all explanations in natural Korean. " +
                "If the user inputs a Korean word, find the best matching English word and explain that English word. " +
                "NEVER translate the input word into Korean for the heading. The heading must always show the English word in its original English form. " +
                "Use markdown formatting.",

            LookupMode.Translate =>
                "You are a professional translator. " +
                "Provide accurate translations with brief notes on nuances when applicable. " +
                "Use markdown formatting for readability.",

            _ => "You are a helpful language assistant."
        };
    }

    private static string BuildDictionaryPrompt(string word, string sourceLanguage, string targetLanguage)
    {
        return $$"""
            [Critical Rules]
            - The heading MUST show the ENGLISH word (e.g., "neighbor", NOT "이웃").
            - IPA must be for the ENGLISH pronunciation (e.g., /ˈneɪbər/, NOT Korean IPA).
            - Korean pronunciation is a phonetic approximation in Hangul (e.g., 네이버).
            - If the user inputs a Korean word, find the matching English word and explain THAT English word.
            - All explanatory text, examples, synonyms, and antonyms must be written in Korean.
            - Synonyms and antonyms must be ENGLISH words with Korean explanations.

            [Formatting Rules]
            - Use rich markdown formatting throughout.
            - Wrap IPA and Korean phonetic in backticks for visual distinction: `/ˈneɪbər/` `네이버`
            - Use a blockquote (>) for the etymology breakdown — start with 📌 and **bold** the root parts.
            - Use a horizontal rule (---) to separate the storytelling from the examples/synonyms section.
            - Use **bold** for key English terms and concepts within storytelling paragraphs.
            - Use *italic* for nuance notes and subtle contextual hints.
            - Add emoji to section headings for visual clarity.

            [Instructions]
            1. Storytelling Format: NEVER use numbers or subtitles in the explanation. Write natural, engaging prose in 3 paragraphs or fewer.
            2. Etymology & Visualization: Break down the English word's roots/prefixes/suffixes inside a blockquote. Provide vivid imagery for intuitive understanding.
            3. Plain Korean: If you use difficult Hanja-based Korean terms, immediately rephrase them in easy everyday Korean.
            4. Native Nuances: Describe how native English speakers actually use this word — contexts, emotional tones, formality, connotations, idioms.
            5. Pronunciation: IPA for the English word + closest Korean phonetic spelling, both in backticks.

            [Output Template]
            ### `/ˈIPA/` `Korean Phonetic`
            **{POS}** {Korean meaning}; **{POS}** {Korean meaning}

            > 📌 **{root/prefix}** ({origin meaning}) + **{root/suffix}** ({origin meaning}): {brief origin story in Korean}

            {3 paragraphs max of storytelling explanation in Korean. Use **bold** for key English terms and *italic* for nuances. No numbers, no subtitles.}

            ---

            ## 활용 예시
            * **{English sentence}** — {Korean translation} *({brief nuance note in Korean})*
            * **{English sentence}** — {Korean translation} *({brief nuance note in Korean})*
            * **{English sentence}** — {Korean translation} *({brief nuance note in Korean})*

            ## 유의어
            * **{English synonym}** `{Korean phonetic}` {POS}. {Korean meaning} — *{nuance difference vs input word, in Korean}*
            * (at least 4)

            ## 반의어
            * **{English antonym}** `{Korean phonetic}` {POS}. {Korean meaning} — *{nuance difference vs input word, in Korean}*
            * (at least 4)

            [Input Word]: {{word}}
            """;
    }

    private static string BuildTranslatePrompt(string text, string sourceLanguage, string targetLanguage)
    {
        return $"""
            Translate the following text from {sourceLanguage} to {targetLanguage}.
            Use rich markdown formatting in your response.

            "{text}"

            [Output Format]
            ### 📝 번역

            (the translation text)

            > 💡 **참고**: (brief notes on nuances, formality level, or alternative translations if applicable)
            """;
    }
}
