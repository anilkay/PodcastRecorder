using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly DeviceCatalog _devices = new();
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IFileDialogs _dialogs;
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _timer;
    private ChannelStripViewModel? _microphoneStrip;
    private bool _initialized;

    public MainViewModel(SettingsStore settingsStore, IFileDialogs dialogs)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _dialogs = dialogs;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        Playlist = new PlaylistViewModel(_engine.Playlist, dialogs, _ui) { AutoAdvance = _settings.AutoAdvance };
        Jingles = new JinglePadViewModel(_engine.Jingles, dialogs, _ui);
        Browser = new FolderBrowserViewModel(dialogs, path => Playlist.AddFiles([path]));
        MasterMeter = new LevelMeterViewModel(_engine.Mixer.MasterMeter);

        _engine.Recorder.Faulted += (_, ex) => _ui.Post(_ => StatusMessage = $"Kayıt hatası: {ex.Message}", null);
        _engine.OutputStopped += (_, ex) =>
        {
            if (ex is not null) _ui.Post(_ => StatusMessage = $"Ses çıkışı durdu: {ex.Message}", null);
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Refresh();
    }

    public PlaylistViewModel Playlist { get; }

    public JinglePadViewModel Jingles { get; }

    public FolderBrowserViewModel Browser { get; }

    public ObservableCollection<ChannelStripViewModel> Channels { get; } = [];

    public LevelMeterViewModel MasterMeter { get; }

    public IReadOnlyList<AudioDevice> InputDevices { get; private set; } = [];

    public IReadOnlyList<AudioDevice> OutputDevices { get; private set; } = [];

    public IReadOnlyList<EncoderOption> Encoders => EncoderCatalog.All;

    [ObservableProperty]
    public partial AudioDevice? SelectedInput { get; set; }

    [ObservableProperty]
    public partial AudioDevice? SelectedOutput { get; set; }

    [ObservableProperty]
    public partial EncoderOption SelectedEncoder { get; set; } = EncoderCatalog.Default;

    [ObservableProperty]
    public partial string RecordingFolder { get; set; } = "";

    [ObservableProperty]
    public partial double MasterVolume { get; set; }

    [ObservableProperty]
    public partial double MonitorVolume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsPaused), nameof(StateText), nameof(PauseButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(TogglePauseCommand), nameof(StopRecordingCommand))]
    public partial RecorderState RecordingState { get; set; }

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "00:00:00";

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string? LastRecordingPath { get; set; }

    public bool IsIdle => RecordingState == RecorderState.Idle;

    public bool IsRecording => RecordingState == RecorderState.Recording;

    public bool IsPaused => RecordingState == RecorderState.Paused;

    public string StateText => RecordingState switch
    {
        RecorderState.Recording => "KAYITTA",
        RecorderState.Paused => "DURAKLATILDI",
        _ => "Hazır",
    };

    public string PauseButtonText => IsPaused ? "Devam" : "Duraklat";

    /// <summary>Opens the devices from the saved settings and starts the audio output.</summary>
    public void Initialize()
    {
        InputDevices = _devices.Inputs;
        OutputDevices = _devices.Outputs;
        OnPropertyChanged(nameof(InputDevices));
        OnPropertyChanged(nameof(OutputDevices));

        Channels.Add(new ChannelStripViewModel(_engine.MusicChannel) { Volume = _settings.MusicVolume });
        Channels.Add(new ChannelStripViewModel(_engine.JingleChannel) { Volume = _settings.JingleVolume });
        MasterVolume = _settings.MasterVolume;
        MonitorVolume = _settings.MonitorVolume;
        SelectedEncoder = EncoderCatalog.Find(_settings.EncoderId);
        RecordingFolder = _settings.RecordingFolder;

        Playlist.AddFiles(_settings.Playlist.Where(File.Exists));
        foreach (var slot in _settings.Jingles)
        {
            if (slot.Index >= 0 && slot.Index < Jingles.Cells.Count && File.Exists(slot.FilePath))
            {
                Jingles.Cells[slot.Index].Assign(slot.FilePath);
            }
        }

        Browser.Navigate(_settings.BrowseFolder is { } folder && Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        _initialized = true;
        SelectedOutput = Pick(OutputDevices, _settings.OutputDeviceId, _devices.DefaultOutputId);
        SelectedInput = Pick(InputDevices, _settings.InputDeviceId, _devices.DefaultInputId);
        _timer.Start();
    }

    partial void OnSelectedOutputChanged(AudioDevice? value)
    {
        if (!_initialized) return;
        try
        {
            if (value is null) _engine.StopOutput();
            else _engine.StartOutput(_devices.GetDevice(value.Id));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Çıkış cihazı açılamadı: {ex.Message}";
        }
    }

    partial void OnSelectedInputChanged(AudioDevice? value)
    {
        if (!_initialized) return;

        double volume = _microphoneStrip?.Volume ?? _settings.MicrophoneVolume;
        if (_microphoneStrip is not null)
        {
            _engine.RemoveMicrophone(_microphoneStrip.Channel);
            Channels.Remove(_microphoneStrip);
            _microphoneStrip = null;
        }

        if (value is null) return;

        try
        {
            var microphone = new MicrophoneSource(_devices.GetDevice(value.Id));
            microphone.Stopped += (_, ex) =>
            {
                if (ex is not null) _ui.Post(_ => StatusMessage = $"Mikrofon durdu: {ex.Message}", null);
            };
            var channel = _engine.AddMicrophone(microphone, "Mikrofon");
            _microphoneStrip = new ChannelStripViewModel(channel) { Volume = volume };
            Channels.Insert(0, _microphoneStrip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mikrofon açılamadı: {ex.Message}";
        }
    }

    partial void OnMasterVolumeChanged(double value) => _engine.Mixer.MasterVolume = (float)value;

    partial void OnMonitorVolumeChanged(double value) => _engine.Mixer.MonitorVolume = (float)value;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void StartRecording()
    {
        if (!_engine.IsOutputRunning)
        {
            StatusMessage = "Kayıt için önce bir çıkış cihazı seçin.";
            return;
        }

        try
        {
            Directory.CreateDirectory(RecordingFolder);
            var encoder = SelectedEncoder.Create();
            var path = Path.Combine(RecordingFolder, $"podcast-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{encoder.FileExtension}");
            _engine.Recorder.Start(path, encoder);
            StatusMessage = $"Kaydediliyor: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kayıt başlatılamadı: {ex.Message}";
        }

        RecordingState = _engine.Recorder.State;
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void TogglePause()
    {
        if (IsPaused) _engine.Recorder.Resume();
        else _engine.Recorder.Pause();
        RecordingState = _engine.Recorder.State;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopRecording()
    {
        var path = await _engine.Recorder.StopAsync();
        RecordingState = _engine.Recorder.State;
        LastRecordingPath = path;
        StatusMessage = path is null ? null : $"Kayıt kaydedildi: {path}";
    }

    [RelayCommand]
    private void ChooseRecordingFolder()
    {
        if (_dialogs.PickFolder(RecordingFolder) is { } folder) RecordingFolder = folder;
    }

    [RelayCommand]
    private void OpenRecordingFolder()
    {
        var target = LastRecordingPath is { } last && File.Exists(last) ? $"/select,\"{last}\"" : $"\"{RecordingFolder}\"";
        Directory.CreateDirectory(RecordingFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }

    private bool CanPause() => !IsIdle;

    private bool CanStop() => !IsIdle;

    private void Refresh()
    {
        foreach (var channel in Channels) channel.Meter.Refresh();
        MasterMeter.Refresh();
        Playlist.Refresh();
        ElapsedText = _engine.Recorder.Elapsed.ToString(@"hh\:mm\:ss");
    }

    private static AudioDevice? Pick(IReadOnlyList<AudioDevice> devices, string? savedId, string? defaultId) =>
        devices.FirstOrDefault(d => d.Id == savedId)
        ?? devices.FirstOrDefault(d => d.Id == defaultId)
        ?? devices.FirstOrDefault();

    public void Dispose()
    {
        _timer.Stop();
        if (!IsIdle)
        {
            _engine.Recorder.StopAsync().GetAwaiter().GetResult();
        }

        SaveSettings();
        _engine.Dispose();
        _devices.Dispose();
    }

    private void SaveSettings()
    {
        _settings.InputDeviceId = SelectedInput?.Id;
        _settings.OutputDeviceId = SelectedOutput?.Id;
        _settings.EncoderId = SelectedEncoder.Id;
        _settings.RecordingFolder = RecordingFolder;
        _settings.BrowseFolder = Browser.CurrentFolder;
        _settings.MicrophoneVolume = _microphoneStrip?.Volume ?? _settings.MicrophoneVolume;
        _settings.MusicVolume = _engine.MusicChannel.Volume;
        _settings.JingleVolume = _engine.JingleChannel.Volume;
        _settings.MasterVolume = MasterVolume;
        _settings.MonitorVolume = MonitorVolume;
        _settings.AutoAdvance = Playlist.AutoAdvance;
        _settings.Playlist = Playlist.Items.Select(i => i.FilePath).ToList();
        _settings.Jingles = Jingles.Cells
            .Where(c => c.FilePath is not null)
            .Select(c => new JingleSlot { Index = c.Index, FilePath = c.FilePath! })
            .ToList();

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing settings is not worth crashing on exit.
        }
    }
}
