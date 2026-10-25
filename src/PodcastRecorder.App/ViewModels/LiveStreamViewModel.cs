using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Streaming;
using PodcastRecorder.App.Localization;

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
                ? Loc.T("Live_ConnectionLostReconnecting")
                : Loc.T("Live_Reconnected");
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
        LiveState.Live => Loc.T("Live_StateLive"),
        LiveState.Connecting => Loc.T("Live_StateConnecting"),
        LiveState.Reconnecting => Loc.T("Live_StateReconnecting"),
        _ => Loc.T("Live_StateOff"),
    };

    public string TargetText => _settings.IsConfigured
        ? $"{_settings.Host}:{_settings.Port}{_settings.Mount} · MP3 {_settings.BitrateKbps} kbps"
        : Loc.T("Live_NoServer");

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
            Message = Loc.T("Live_NeedOutput");
            return;
        }

        if (Secrets.Unprotect(_settings.ProtectedPassword) is not { } password)
        {
            Message = Loc.T("Live_PasswordUnreadable");
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
            Message = Loc.F("Live_Started", target);
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
        Message = Loc.T("Live_Stopped");
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
            Message = Loc.T("Live_SlowConnection");
        }
    }
}
