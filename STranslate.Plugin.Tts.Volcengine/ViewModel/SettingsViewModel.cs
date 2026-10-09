using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace STranslate.Plugin.Tts.Volcengine.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly Main _main;
    private bool _isUpdating;

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        _main = main;

        Url = settings.Url;

        // 旧配置里可能残留被污染的展示文本（会被服务端判 400），加载时就地清洗
        Speaker = VolcTtsProtocol.NormalizeSpeakerId(settings.Speaker);
        settings.Speaker = Speaker;
        Speakers = new ObservableCollection<SpeakerItem>(settings.Speakers);
        TimeoutSeconds = settings.TimeoutSeconds;

        // 「自动」始终置顶，且随界面语言刷新文案
        AutoSpeaker = new SpeakerItem(VolcTtsProtocol.AutoSpeakerId,
            context.GetTranslation("STranslate_Plugin_Tts_Volcengine_SpeakerAuto"));
        Speakers.Insert(0, AutoSpeaker);

        PropertyChanged += OnPropertyChanged;
        Speakers.CollectionChanged += OnSpeakersCollectionChanged;
    }

    /// <summary>
    ///     「自动（跟随文本语言）」项。
    /// </summary>
    public SpeakerItem AutoSpeaker { get; }

    [ObservableProperty] public partial string Url { get; set; } = string.Empty;

    /// <summary>
    ///     当前音色 ID。永远只存 ID，绝不存展示文本。
    /// </summary>
    [ObservableProperty] public partial string Speaker { get; set; } = VolcTtsProtocol.AutoSpeakerId;
    [ObservableProperty] public partial ObservableCollection<SpeakerItem> Speakers { get; set; } = [];
    [ObservableProperty] public partial int TimeoutSeconds { get; set; } = 30;

    [ObservableProperty] public partial string ValidateResult { get; set; } = string.Empty;

    [RelayCommand]
    private void AddSpeaker(string speakerId)
    {
        if (_isUpdating || string.IsNullOrWhiteSpace(speakerId))
            return;

        speakerId = speakerId.Trim();
        using var _ = new UpdateGuard(this);
        if (Speakers.All(s => s.Id != speakerId))
            Speakers.Add(new SpeakerItem(speakerId, speakerId));

        // UpdateGuard 会屏蔽属性变更回写，这里必须显式落盘，否则新增音色不会被持久化
        Speaker = speakerId;
        _settings.Speaker = speakerId;
        _context.SaveSettingStorage<Settings>();
    }

    [RelayCommand]
    private void DeleteSpeaker(string speakerId)
    {
        if (_isUpdating || string.IsNullOrWhiteSpace(speakerId) || speakerId == VolcTtsProtocol.AutoSpeakerId)
            return;

        var item = Speakers.FirstOrDefault(s => s.Id == speakerId);
        if (item is null)
            return;

        using var _ = new UpdateGuard(this);
        if (Speaker == speakerId)
        {
            // 同上：删除当前音色时要显式回落到「自动」并落盘
            Speaker = VolcTtsProtocol.AutoSpeakerId;
            _settings.Speaker = VolcTtsProtocol.AutoSpeakerId;
        }

        Speakers.Remove(item);
        _context.SaveSettingStorage<Settings>();
    }

    [RelayCommand]
    private async Task ValidateAsync()
    {
        try
        {
            await _main.ValidateAsync();
            ValidateResult = _context.GetTranslation("ValidationSuccess");
        }
        catch (Exception ex)
        {
            ValidateResult = _context.GetTranslation("ValidationFailure");
            _context.Logger.LogError(ex, ValidateResult);
            _context.Snackbar.ShowError($"{ValidateResult}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        PropertyChanged -= OnPropertyChanged;
        Speakers.CollectionChanged -= OnSpeakersCollectionChanged;
    }

    private void OnSpeakersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or
            NotifyCollectionChangedAction.Remove or
            NotifyCollectionChangedAction.Replace)
        {
            _settings.Speakers = [.. Speakers.Where(s => s.Id != VolcTtsProtocol.AutoSpeakerId)];
            _context.SaveSettingStorage<Settings>();
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating)
            return;

        switch (e.PropertyName)
        {
            case nameof(Url):
                _settings.Url = Url;
                break;
            case nameof(Speaker):
                // 防御：可编辑 ComboBox 失焦时可能把展示文本写进 Speaker，
                // 这里做一次归一化，确保落盘的永远是合法音色 ID（或空=自动）。
                Speaker = NormalizeSpeakerId(Speaker);
                _settings.Speaker = Speaker;
                break;
            case nameof(TimeoutSeconds):
                _settings.TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 120);
                break;
            default:
                return;
        }

        _context.SaveSettingStorage<Settings>();
    }

    /// <summary>
    ///     把可能是展示文本的值归一化为合法音色 ID。
    /// </summary>
    /// <remarks>
    ///     可编辑 ComboBox 在 IsEditable=True 时，SelectedValue 偶发会取到 Text（即 ToString() 结果）。
    ///     一旦把展示文本当 speaker 发给火山，服务端直接返回 base_resp 400。
    ///     这里做兜底：命中已知音色就还原成 ID，命中「自动」就还原成空，其余原样保留（允许用户手写自定义 ID）。
    /// </remarks>
    internal string NormalizeSpeakerId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return VolcTtsProtocol.AutoSpeakerId;

        var trimmed = value.Trim();

        // 命中列表里的音色（按 ID 或展示名匹配）
        foreach (var item in Speakers)
        {
            if (string.Equals(item.Id, trimmed, StringComparison.Ordinal))
                return item.Id;

            if (!string.IsNullOrWhiteSpace(item.Name) &&
                (trimmed == item.Name || trimmed == item.ToString() || trimmed.EndsWith(" · " + item.Id, StringComparison.Ordinal)))
                return item.Id;
        }

        // 「自动（跟随文本语言）」的各种形态
        if (trimmed.StartsWith(AutoSpeaker.Name, StringComparison.Ordinal))
            return VolcTtsProtocol.AutoSpeakerId;

        return trimmed;
    }

    private readonly struct UpdateGuard : IDisposable
    {
        private readonly SettingsViewModel _viewModel;

        public UpdateGuard(SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel._isUpdating = true;
        }

        public void Dispose() => _viewModel._isUpdating = false;
    }
}
