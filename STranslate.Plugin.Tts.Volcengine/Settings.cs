namespace STranslate.Plugin.Tts.Volcengine;

public class Settings
{
    /// <summary>
    ///     合成端点。留空回退到默认端点。
    /// </summary>
    public string Url { get; set; } = VolcTtsProtocol.DefaultUrl;

    /// <summary>
    ///     音色 ID。为空表示「自动（跟随文本语言）」。
    /// </summary>
    public string Speaker { get; set; } = VolcTtsProtocol.AutoSpeakerId;

    /// <summary>
    ///     可选音色列表，用户可自行增删。
    /// </summary>
    public List<SpeakerItem> Speakers { get; set; } = [.. VolcTtsProtocol.DefaultSpeakers];

    /// <summary>
    ///     单次请求超时（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
