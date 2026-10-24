using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.App.ViewModels;

/// <summary>The "Canlı yayın" panel: Icecast settings, going live and the on-air status.</summary>
public partial class LiveStreamViewModel : ObservableObject
{
    private readonly LiveStreamer _streamer;
    private readonly LiveStreamSettings _settings;
    private readonly ILiveSettingsDialog _dialog;
    private readonly Func<bool> _isOutputRunning;
    private readonly SynchronizationContext _ui;
    private string? _title;
    private long _reportedDrops;

    public LiveStreamViewModel(
        LiveStreamer streamer,
        LiveStreamSettings settings,
        ILiveSettingsDialog dialog,
        Func<bool> isOutputRunning,
        SynchronizationContext ui)
    {
        _streamer = streamer;
        _settings = settings;
        _dialog = dialog;
        _isOutputRunning = isOutputRunning;
        _ui = ui;
        _streamer.Faulted += (_, ex) => _ui.Post(_ =>
        {
            State = _streamer.State;
            Message = ex.Message;
        }, null);
        _streamer.StateChanged += (_, state) => _ui.Post(_ =>
        {
            State = state;
            Message = state == LiveState.Reconnecting
                ? "Bağlantı koptu, yeniden bağlanılıyor…"
                : "Bağlantı yeniden kuruldu, yayın sürüyor.";
        }, null);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(IsOffline), nameof(CanStop), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(EditSettingsCommand))]
    public partial LiveState State { get; set; }

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "00:00:00";

    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool IsLive => State == LiveState.Live;

    public bool IsOffline => State == LiveState.Offline;

    /// <summary>Live or trying to get back on air.</summary>
    public bool CanStop => State is LiveState.Live or LiveState.Reconnecting;

    public string StateText => State switch
    {
        LiveState.Live => "YAYINDA",
        LiveState.Connecting => "Bağlanıyor…",
        LiveState.Reconnecting => "Yeniden bağlanıyor…",
        _ => "Kapalı",
    };

    public string TargetText => _settings.IsConfigured
        ? $"{_settings.Host}:{_settings.Port}{_settings.Mount} · MP3 {_settings.BitrateKbps} kbps"
        : "Sunucu ayarlanmadı";

    [RelayCommand(CanExecute = nameof(IsOffline))]
    private async Task Start()
    {
        if (!_settings.IsConfigured && !_dialog.Edit(_settings))
        {
            return;
        }

        OnPropertyChanged(nameof(TargetText));
        if (!_isOutputRunning())
        {
            Message = "Yayın için önce bir dinleme çıkışı seçin; sesi o cihazın saati yürütür.";
            return;
        }

        if (Secrets.Unprotect(_settings.ProtectedPassword) is not { } password)
        {
            Message = "Kayıtlı şifre okunamadı; ayarlardan şifreyi yeniden girin.";
            return;
        }

        var target = new IcecastSettings(
            _settings.Host, _settings.Port, _settings.Mount, _settings.User, password, _settings.BitrateKbps, _settings.StreamName);
        Message = null;
        _reportedDrops = 0;
        ElapsedText = "00:00:00";
        State = LiveState.Connecting;
        try
        {
            await _streamer.StartAsync(target);
            Message = $"Yayın başladı: {target}";
            if (_title is not null) _ = _streamer.UpdateTitleAsync(_title);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            Message = ex.Message;
        }

        State = _streamer.State;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task Stop()
    {
        await _streamer.StopAsync();
        State = _streamer.State;
        Message = "Yayın kapatıldı.";
    }

    [RelayCommand(CanExecute = nameof(IsOffline))]
    private void EditSettings()
    {
        if (_dialog.Edit(_settings))
        {
            OnPropertyChanged(nameof(TargetText));
            Message = null;
        }
    }

    /// <summary>Tells listeners what is playing now (the playlist's current song).</summary>
    public void SetNowPlaying(string? title)
    {
        _title = title;
        if (title is not null) _ = _streamer.UpdateTitleAsync(title);
    }

    /// <summary>Called by the UI timer.</summary>
    public void Refresh()
    {
        if (!IsLive) return;
        ElapsedText = _streamer.Elapsed.ToString(@"hh\:mm\:ss");
        long dropped = _streamer.DroppedBlocks;
        if (dropped > _reportedDrops)
        {
            _reportedDrops = dropped;
            Message = "Bağlantı yavaş: sesin bir kısmı dinleyicilere ulaşmadı.";
        }
    }
}
