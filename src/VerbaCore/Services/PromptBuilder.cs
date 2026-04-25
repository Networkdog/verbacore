using System.Text.RegularExpressions;
using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed partial class PromptBuilder
{
    /// <summary>
    /// Auto-selects the best mode for the given input:
    /// - Natural language words/phrases (≤3 words) → Dictionary
    /// - Natural language sentences (>3 words) → Translate
    /// - Code, formulas, URLs, structured data, etc. → Assist
    /// </summary>
    public static LookupMode AutoSelectMode(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return LookupMode.Dictionary;

        // Detect non-language content → Assist mode
        if (LooksLikeNonLanguage(trimmed))
            return LookupMode.Assist;

        var wordCount = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount <= 3 ? LookupMode.Dictionary : LookupMode.Translate;
    }

    /// <summary>
    /// Heuristic: returns true if the input looks like code, a formula, a URL,
    /// a file path, structured data, or other non-natural-language content.
    /// </summary>
    private static bool LooksLikeNonLanguage(string input)
    {
        // URL or URI scheme
        if (UrlPattern().IsMatch(input)) return true;

        // File paths (Windows or Unix)
        if (FilePathPattern().IsMatch(input)) return true;

        // Code-like: contains braces, semicolons, arrows, assignment operators, etc.
        // Threshold: ≥ 2 code indicators in the input
        var codeIndicators = 0;
        if (input.Contains('{') || input.Contains('}')) codeIndicators++;
        if (input.Contains(';') && !input.EndsWith(';')) codeIndicators++; // semicolons mid-text
        if (input.Contains("=>") || input.Contains("->")) codeIndicators++;
        if (input.Contains("==") || input.Contains("!=") || input.Contains("&&") || input.Contains("||")) codeIndicators++;
        if (input.Contains("()") || input.Contains("();")) codeIndicators++;
        if (input.Contains("/**") || input.Contains("///") || input.Contains("//") || input.Contains("/*")) codeIndicators++;
        if (input.Contains("def ") || input.Contains("class ") || input.Contains("function ")
            || input.Contains("import ") || input.Contains("using ") || input.Contains("#include")) codeIndicators++;
        if (codeIndicators >= 2) return true;

        // Math/formula: lots of operators, digits, and symbols
        var symbolRatio = (double)input.Count(c => "=+*/<>^|&%$#@~`\\[]".Contains(c)) / input.Length;
        if (symbolRatio > 0.15 && input.Length > 5) return true;

        // Stack traces, error messages with line numbers
        if (StackTracePattern().IsMatch(input)) return true;

        // JSON/XML-like
        if ((input.StartsWith('{') && input.EndsWith('}')) ||
            (input.StartsWith('[') && input.EndsWith(']')) ||
            (input.StartsWith('<') && input.EndsWith('>'))) return true;

        return false;
    }

    [GeneratedRegex(@"^https?://|^ftp://|^file://|^[a-zA-Z][a-zA-Z0-9+.-]*://", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"^[A-Za-z]:\\|^/[a-z]|^\./|^\.\./|^~/", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathPattern();

    [GeneratedRegex(@"at .+\.(cs|java|py|js|ts|cpp|go|rb):\d+|line \d+|\.cs\(\d+|Exception:|Traceback", RegexOptions.IgnoreCase)]
    private static partial Regex StackTracePattern();

    public string Build(string input, LookupMode mode, string nativeLanguage, string foreignLanguage)
    {
        return mode switch
        {
            LookupMode.Dictionary => BuildDictionaryPrompt(input, nativeLanguage, foreignLanguage),
            LookupMode.Translate => BuildTranslatePrompt(input, nativeLanguage, foreignLanguage),
            LookupMode.Assist => BuildAssistPrompt(input, nativeLanguage, foreignLanguage),
            _ => BuildDictionaryPrompt(input, nativeLanguage, foreignLanguage)
        };
    }

    public string GetSystemMessage(LookupMode mode, string nativeLanguage, string foreignLanguage)
    {
        return mode switch
        {
            LookupMode.Dictionary =>
                $"You are a top-tier linguist and etymology expert. " +
                $"The user's native language is {nativeLanguage} and their primary foreign language is {foreignLanguage}. " +
                $"When the user inputs a {foreignLanguage} word, explain it in {nativeLanguage}. " +
                $"When the user inputs a {nativeLanguage} word, find the best matching {foreignLanguage} word and explain that word in {nativeLanguage}. " +
                $"When the user inputs a word in any other language, explain it in {nativeLanguage} with references to {foreignLanguage} equivalents where helpful. " +
                $"Keep the original word as-is in the heading. Provide IPA for the original word's pronunciation. Write all explanations in natural {nativeLanguage}. " +
                $"NEVER translate the input word for the heading. The heading must always show the word in its original form. " +
                "Use markdown formatting.",

            LookupMode.Translate =>
                $"You are a professional translator. " +
                $"The user's native language is {nativeLanguage} and their primary foreign language is {foreignLanguage}. " +
                $"When the user inputs text in {foreignLanguage}, translate it to {nativeLanguage}. " +
                $"When the user inputs text in {nativeLanguage}, translate it to {foreignLanguage}. " +
                $"When the user inputs text in any other language, translate it to {nativeLanguage}. " +
                $"Provide accurate translations with brief notes on nuances when applicable. " +
                "Use markdown formatting for readability.",

            LookupMode.Assist =>
                $"You are a knowledgeable assistant. The user's native language is {nativeLanguage}. " +
                $"The user has selected some text (code, a URL, an error message, a formula, or other non-language content) and wants to understand it. " +
                $"Infer what the user most likely wants to know about the given text, and answer that question directly in {nativeLanguage}. " +
                $"Be concise but thorough. Use markdown formatting.",

            _ => "You are a helpful assistant."
        };
    }

    private static string BuildDictionaryPrompt(string word, string nativeLanguage, string foreignLanguage)
    {
        return $$"""
            [Critical Rules]
            - The user's native language is {{nativeLanguage}}. Their primary foreign language is {{foreignLanguage}}.
            - Auto-detect the input language. Apply these rules:
              • If the input is in {{foreignLanguage}}: explain the {{foreignLanguage}} word in {{nativeLanguage}}.
              • If the input is in {{nativeLanguage}}: find the best matching {{foreignLanguage}} word and explain THAT word in {{nativeLanguage}}.
              • If the input is in a THIRD language: explain the word in {{nativeLanguage}}, with {{foreignLanguage}} equivalents where helpful.
            - Do NOT repeat the input word as a heading or title — the UI already displays it.
            - Start directly with the IPA pronunciation line.
            - IPA must be for the ORIGINAL word's pronunciation.
            - Provide a phonetic approximation in {{nativeLanguage}} script if applicable.
            - All explanatory text, examples, synonyms, and antonyms must be written in {{nativeLanguage}}.
            - Synonyms and antonyms should be in the word's original language with {{nativeLanguage}} explanations.

            [Formatting Rules]
            - Use rich markdown formatting throughout.
            - Wrap IPA and phonetic approximation in backticks for visual distinction.
            - Use a blockquote (>) for the etymology breakdown — start with 📌 and **bold** the root parts.
            - Use a horizontal rule (---) to separate the storytelling from the examples/synonyms section.
            - Use **bold** for key terms and concepts within storytelling paragraphs.
            - Use *italic* for nuance notes and subtle contextual hints.
            - Add emoji to section headings for visual clarity.

            [Instructions]
            1. Storytelling Format: NEVER use numbers or subtitles in the explanation. Write natural, engaging prose in 3 paragraphs or fewer.
            2. Etymology & Visualization: Break down the word's roots/prefixes/suffixes inside a blockquote. Provide vivid imagery for intuitive understanding.
            3. Plain Language: If you use difficult or technical terms in {{nativeLanguage}}, immediately rephrase them in easy everyday {{nativeLanguage}}.
            4. Native Nuances: Describe how native speakers actually use this word — contexts, emotional tones, formality, connotations, idioms.
            5. Pronunciation: IPA for the word + closest phonetic spelling in {{nativeLanguage}} script, both in backticks.

            [Output Template]
            `/ˈIPA/` `phonetic in {{nativeLanguage}}`
            **{POS}** {meaning in {{nativeLanguage}}}; **{POS}** {meaning in {{nativeLanguage}}}

            > 📌 **{root/prefix}** ({origin meaning}) + **{root/suffix}** ({origin meaning}): {brief origin story in {{nativeLanguage}}}

            {3 paragraphs max of storytelling explanation in {{nativeLanguage}}. Use **bold** for key terms and *italic* for nuances. No numbers, no subtitles.}

            ---

            ## 활용 예시
            * **{example sentence}** — {translation in {{nativeLanguage}}} *({brief nuance note})*
            * **{example sentence}** — {translation in {{nativeLanguage}}} *({brief nuance note})*
            * **{example sentence}** — {translation in {{nativeLanguage}}} *({brief nuance note})*

            [Input Word]: {{word}}
            """;
    }

    private static string BuildTranslatePrompt(string text, string nativeLanguage, string foreignLanguage)
    {
        return $"""
            [Rules]
            - The user's native language is {nativeLanguage}. Their primary foreign language is {foreignLanguage}.
            - Auto-detect the input language and apply:
              • If input is in {foreignLanguage} → translate to {nativeLanguage}.
              • If input is in {nativeLanguage} → translate to {foreignLanguage}.
              • If input is in a THIRD language → translate to {nativeLanguage}.
            - Use rich markdown formatting in your response.

            [Input Text]
            "{text}"

            [Output Format]
            ### 📝 번역

            (the translation text)

            > 💡 **참고**: (brief notes on nuances, formality level, or alternative translations if applicable, written in {nativeLanguage})
            """;
    }

    private static string BuildAssistPrompt(string text, string nativeLanguage, string foreignLanguage)
    {
        return $"""
            [Rules]
            - The user's native language is {nativeLanguage}.
            - The user has selected the following text and wants to understand it.
            - Analyze the text and determine what it is (code, error message, URL, formula, config, log, data, etc.).
            - Infer the most useful question the user would ask about this text, and answer it.
            - Examples of useful responses:
              • Code snippet → explain what it does, key logic, potential issues
              • Error/stack trace → explain the cause and suggest fixes
              • URL → describe what the link points to and its purpose
              • Formula/expression → explain the meaning and result
              • Config/JSON/XML → explain the structure and key settings
              • Log output → summarize what happened and highlight important entries
            - Write the entire response in {nativeLanguage}.
            - Use rich markdown formatting for readability.
            - Be concise but thorough — aim for practical, actionable insight.

            [Input Text]
            ```
            {text}
            ```

            [Output Format]
            ### 💡 (a short title describing what the input is, in {nativeLanguage})

            (clear, practical explanation in {nativeLanguage})
            """;
    }
}
