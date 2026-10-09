using System.Windows.Controls;

namespace STranslate.Plugin.Tts.Volcengine;

public class Main : ITtsPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
    }

    public void Dispose() => _viewModel?.Dispose();

    public async Task PlayAudioAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var segments = VolcTtsProtocol.SplitText(text);
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var audio = await SynthesizeAsync(segment, cancellationToken);

            // 火山固定返回带 ID3 头的 MP3（24 kHz 单声道），显式声明格式，不让宿主猜测
            await Context.AudioPlayer.PlayAsync(new AudioData(audio, AudioFormat.Mp3), cancellationToken);
        }
    }

    /// <summary>
    ///     连通性验证：合成一小段文本，不播放。
    /// </summary>
    internal async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        _ = await SynthesizeAsync("Hello", cancellationToken);
    }

    private async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(Settings.Url) ? VolcTtsProtocol.DefaultUrl : Settings.Url.Trim();

        var content = VolcTtsProtocol.CreateRequest(text, Settings.Speaker);
        var options = VolcTtsProtocol.CreateOptions(Settings.TimeoutSeconds);

        var response = await Context.HttpService.PostAsync(url, content, options, cancellationToken);

        var error = VolcTtsProtocol.TryGetAudio(response, out var audio);
        if (audio is null)
            throw new InvalidOperationException(DescribeFailure(error, text));

        return audio;
    }

    /// <summary>
    ///     把失败码翻译成用户能看懂的提示。自动音色模式下，服务端对不支持的语言会直接 400，
    ///     这里按字符区间给出明确原因，避免只抛一个无意义的 400。
    /// </summary>
    private string DescribeFailure(string? error, string text)
    {
        if (string.IsNullOrWhiteSpace(Settings.Speaker))
        {
            if (VolcTtsProtocol.IsCyrillic(text))
                return Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_UnsupportedCyrillic");
            if (VolcTtsProtocol.IsArabic(text))
                return Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_UnsupportedArabic");
        }

        return error switch
        {
            "empty-response" => Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_EmptyResponse"),
            "invalid-json" => Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_InvalidJson"),
            "empty-audio" or "invalid-base64" => Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_EmptyAudio"),
            _ => string.IsNullOrWhiteSpace(error)
                ? Context.GetTranslation("STranslate_Plugin_Tts_Volcengine_UnknownError")
                : error
        };
    }
}
