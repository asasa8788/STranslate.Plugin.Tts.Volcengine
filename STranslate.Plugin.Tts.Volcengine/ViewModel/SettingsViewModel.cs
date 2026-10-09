using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        Speaker = settings.Speaker;
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
