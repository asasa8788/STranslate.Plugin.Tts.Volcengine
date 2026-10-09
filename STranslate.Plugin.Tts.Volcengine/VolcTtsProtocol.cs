using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace STranslate.Plugin.Tts.Volcengine;

/// <summary>
///     音色项：音色 ID + 展示名。
/// </summary>
public class SpeakerItem
{
    /// <summary>
    ///     音色 ID。为空表示「自动」，即不向服务端传 speaker，由服务端按文本语言选择。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     展示名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public SpeakerItem() { }

    public SpeakerItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} · {Id}";
}

/// <summary>
///     火山翻译 TTS 协议封装（纯静态，不做任何 I/O）。
/// </summary>
/// <remarks>
///     端点为火山翻译 Chrome 扩展使用的内部接口，不是开放平台 API，无需 AccessKey / 签名。
///     请求体只接受 text / speaker / language 三个字段，多传任何字段都会返回 HTTP 400。
/// </remarks>
internal static class VolcTtsProtocol
{
    /// <summary>
    ///     默认端点。
    /// </summary>
    internal const string DefaultUrl = "https://translate.volcengine.com/crx/tts/v1/";

    /// <summary>
    ///     单次请求的文本硬上限（实测 2000 通过、2001 拒绝）。
    /// </summary>
    internal const int MaxTextLength = 2000;

    /// <summary>
    ///     空音色表示「自动」。
    /// </summary>
    internal const string AutoSpeakerId = "";

    private const string Origin = "chrome-extension://klgfhbdadaspgppeadghjjemk";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

    /// <summary>
    ///     已验证可用（2026-09 实测）的音色列表。
    ///     韩语女声 BV059、俄语女声 BV068、阿语男声 BV570 已从服务端下线，故不在列表中。
    /// </summary>
    internal static readonly SpeakerItem[] DefaultSpeakers =
    [
        new("zh_male_rap", "中文·嘻哈歌手"),
        new("zh_female_sichuan", "中文·四川女声"),
        new("tts.other.BV021_streaming", "中文·东北男声"),
        new("tts.other.BV026_streaming", "中文·粤语男声"),
        new("tts.other.BV025_streaming", "中文·台湾女声"),
        new("zh_male_xiaoming", "中文·影视配音"),
        new("zh_male_zhubo", "中文·男主播"),
        new("zh_female_zhubo", "中文·女主播"),
        new("zh_female_qingxin", "中文·清新女声"),
        new("zh_female_story", "中文·少儿故事"),
        new("en_male_adam", "英语·美式男声"),
        new("tts.other.BV027_streaming", "英语·美式女声"),
        new("en_male_bob", "英语·英式男声"),
        new("tts.other.BV032_TOBI_streaming", "英语·英式女声"),
        new("tts.other.BV516_streaming", "英语·澳洲男声"),
        new("en_female_sarah", "英语·澳洲女声"),
        new("jp_male_satoshi", "日语·男声"),
        new("jp_female_mai", "日语·女声"),
        new("kr_male_gye", "韩语·男声"),
        new("fr_male_enzo", "法语·男声"),
        new("tts.other.BV078_streaming", "法语·女声"),
        new("es_male_george", "西语·男声"),
        new("tts.other.BV065_streaming", "西语·女声"),
        new("de_female_sophie", "德语·女声"),
        new("tts.other.BV087_streaming", "意语·男声"),
        new("tts.other.BV083_streaming", "土耳其语·男声"),
        new("tts.other.BV531_streaming", "葡语·男声"),
        new("pt_female_alice", "葡语·女声"),
        new("tts.other.BV075_streaming", "越南语·男声"),
        new("tts.other.BV074_streaming", "越南语·女声"),
        new("tts.other.BV092_streaming", "马来语·女声"),
        new("tts.other.BV160_streaming", "印尼语·男声"),
        new("id_female_noor", "印尼语·女声"),
    ];

    /// <summary>
    ///     构造请求选项（浏览器伪装头 + 超时）。
    /// </summary>
    internal static Options CreateOptions(int timeoutSeconds)
    {
        return new Options
        {
            Headers = new Dictionary<string, string>
            {
                { "authority", "translate.volcengine.com" },
                { "origin", Origin },
                { "accept", "application/json, text/plain, */*" },
                { "sec-fetch-dest", "empty" },
                { "sec-fetch-mode", "cors" },
                { "sec-fetch-site", "none" },
                { "cookie", "hasUserBehavior=1" },
                { "user-agent", UserAgent }
            },
            ContentType = "application/json",
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 30 : timeoutSeconds, 5, 120))
        };
    }

    /// <summary>
    ///     构造请求体。音色为空时不传 speaker，交由服务端按文本语言自动选择。
    /// </summary>
    internal static JsonObject CreateRequest(string text, string? speaker)
    {
        var request = new JsonObject { ["text"] = text };

        if (!string.IsNullOrWhiteSpace(speaker) && speaker != AutoSpeakerId)
            request["speaker"] = speaker.Trim();

        return request;
    }

    /// <summary>
    ///     解析响应。
    /// </summary>
    /// <param name="response">响应正文。</param>
    /// <param name="audio">成功时返回 MP3 字节。</param>
    /// <returns>失败原因；成功返回 null。</returns>
    internal static string? TryGetAudio(string? response, out byte[]? audio)
    {
        audio = null;

        if (string.IsNullOrWhiteSpace(response))
            return "empty-response";

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(response);
        }
        catch (JsonException)
        {
            return "invalid-json";
        }

        var statusCode = root?["base_resp"]?["status_code"]?.GetValue<int>();
        if (statusCode is not 0)
        {
            var message = root?["base_resp"]?["status_message"]?.ToString();
            return string.IsNullOrWhiteSpace(message) ? $"status-{statusCode}" : message;
        }

        var data = root?["audio"]?["data"]?.ToString();
        if (string.IsNullOrWhiteSpace(data))
            return "empty-audio";

        try
        {
            audio = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return "invalid-base64";
        }

        return audio.Length == 0 ? "empty-audio" : null;
    }

    /// <summary>
    ///     判断文本是否主要由西里尔字母构成（俄语等）。
    /// </summary>
    internal static bool IsCyrillic(string text)
    {
        var letters = 0;
        var cyrillic = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
                continue;
            letters++;
            if (ch is >= '\u0400' and <= '\u04FF')
                cyrillic++;
        }

        return letters > 0 && (double)cyrillic / letters > 0.5;
    }

    /// <summary>
    ///     判断文本是否主要由阿拉伯字母构成。
    /// </summary>
    internal static bool IsArabic(string text)
    {
        var letters = 0;
        var arabic = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
                continue;
            letters++;
            if (ch is >= '\u0600' and <= '\u06FF')
                arabic++;
        }

        return letters > 0 && (double)arabic / letters > 0.5;
    }

    /// <summary>
    ///     按句切分超长文本，保证每段不超过 <see cref="MaxTextLength" />。
    /// </summary>
    /// <remarks>
    ///     优先在句末标点后断句；单个句子本身超限时退化为按长度硬切，避免丢字。
    /// </remarks>
    internal static List<string> SplitText(string text, int maxLength = MaxTextLength)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        if (text.Length <= maxLength)
        {
            result.Add(text);
            return result;
        }

        var sentenceEndings = new[] { '。', '！', '？', '；', '.', '!', '?', '\n', '\r' };
        var builder = new StringBuilder();

        foreach (var ch in text)
        {
            builder.Append(ch);

            if (builder.Length >= maxLength || (sentenceEndings.Contains(ch) && builder.Length >= maxLength * 3 / 4))
            {
                var segment = builder.ToString().Trim();
                if (segment.Length > 0)
                    result.Add(segment);
                builder.Clear();
            }
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
            result.Add(tail);

        // 单句本身超限（无标点长串）时兜底硬切
        var final = new List<string>();
        foreach (var segment in result)
        {
            if (segment.Length <= maxLength)
            {
                final.Add(segment);
                continue;
            }

            for (var i = 0; i < segment.Length; i += maxLength)
                final.Add(segment.Substring(i, Math.Min(maxLength, segment.Length - i)));
        }

        return final;
    }
}
