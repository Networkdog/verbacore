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
                "당신은 한국어 원어민이 영단어의 진짜 의미와 뉘앙스를 완벽하게 체화할 수 있도록 돕는 최고 수준의 언어학자이자 어원 해설가입니다. " +
                "단순히 뜻을 나열하는 전통적 사전의 한계를 벗어나, 단어의 탄생 배경과 네이티브의 감각을 이야기(Storytelling) 형식으로 전달해야 합니다. " +
                "Markdown 형식으로 응답하세요.",

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
            다음 지침에 따라 <입력 단어>에 대한 사전 컨텐츠를 생성하세요.

            <작성 지침>
            0. 영어 단어를 물어볼 때는 영어 단어 해설을, 한국어 단어를 물어볼 때는 한국어에 해당하는 영어 단어 해설을 한다.
            1. 형식 준수: 제공된 <출력 템플릿>의 구조를 정확히 따르세요.
            2. 이야기식 해설 (가장 중요): <의미 해설> 섹션에서는 1, 2, 3과 같은 번호 매기기나 소제목을 절대 사용하지 마세요. 문단 나누기만 활용하여 하나의 자연스럽고 흥미로운 칼럼이나 이야기처럼 줄글로 작성하세요.
            3. 어원과 시각화: 단어의 뼈대(어원, 접두사/접미사 분해 등)를 설명하고, 머릿속에 그림이 그려지도록 시각적인 이미지를 제시하여 암기가 아닌 '이해'를 도우세요.
            4. 어려운 번역어 풀이: 사전적 의미가 어려운 한자어(예: 위용, 이행하다, 타당성 등)인 경우, 그 한자어의 뜻을 일상적이고 쉬운 한국어로 다시 풀어서 설명하세요. 한국어를 몰라서 영어를 이해하지 못하는 일이 없어야 합니다.
            5. 네이티브의 뉘앙스: 실제 원어민들이 어떤 상황에서, 어떤 감정(격식/비격식, 긍정/부정적 함의 등)을 담아 사용하는지, 관용적으로 어떻게 쓰이는지 생생하게 설명하세요.
            6. 유의어/반의어: 반드시 각각 최소 4개씩 제시하세요. 단순히 단어만 나열하지 말고, 메인 단어와 비교하여 미묘한 뉘앙스 차이가 무엇인지 짧게 덧붙이세요.
            7. 발음: 국제음성기호(IPA)와 함께, 한국인이 읽었을 때 가장 원어민 발음에 가까운 한글 표기를 추가하세요.

            <출력 템플릿>
            ## <입력 단어> [<발음기호>, <한국어 발음>] <품사(n|v|...)>. <사전적 의미>; <품사(n|v|...)>. <사전적 의미> …

            (여기에 번호와 소제목 없이 이야기식 해설 작성, 3문단 이하로)

            <활용 예시>
            * <문장1> <문장 해석> (<해설>)
            * <문장2> <문장 해석> (<해설>)
            * <문장3> <문장 해석> (<해설>)

            <유의어>
            * **<단어1>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어2>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어3>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어4>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)

            <반의어>
            * **<단어1>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어2>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어3>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)
            * **<단어4>** [<한국어 발음>] <품사>. <단어 의미> (<입력 단어와 비교한 뉘앙스 차이 설명>)

            <입력 단어>: {word}
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
