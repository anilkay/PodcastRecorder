using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.Mixing;
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
    private readonly List<ChannelStripViewModel> _microphoneStrips = [];
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

    /// <summary>The device picked in the "add microphone" box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMicrophoneCommand))]
    public partial AudioDevice? InputToAdd { get; set; }

    /// <summary>Also write every mixer channel to its own file (multitrack).</summary>
    [ObservableProperty]
    public partial bool RecordStems { get; set; }

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

    /// <summary>The on-air switch: off silences the microphone, on opens it and ducks the music.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicrophoneButtonText))]
    public partial bool IsMicrophoneLive { get; set; } = true;

    [ObservableProperty]
    public partial bool DuckingEnabled { get; set; }

    /// <summary>Music level under the live microphone, in dB.</summary>
    [ObservableProperty]
    public partial double DuckLevelDb { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsPaused), nameof(StateText), nameof(PauseButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(TogglePauseCommand), nameof(StopRecordingCommand), nameof(AddMicrophoneCommand))]
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

    public string MicrophoneButtonText => IsMicrophoneLive ? "Mikrofon AÇIK" : "Mikrofon KAPALI";

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
        DuckingEnabled = _settings.DuckingEnabled;
        DuckLevelDb = _settings.DuckLevelDb;
        _engine.DuckingEnabled = DuckingEnabled;
        _engine.DuckLevelDb = DuckLevelDb;
        Playlist.CrossfadeSeconds = _settings.CrossfadeSeconds;
        SelectedEncoder = EncoderCatalog.Find(_settings.EncoderId);
        RecordingFolder = _settings.RecordingFolder;
        RecordStems = _settings.RecordStems;

        Playlist.AddFiles(_settings.Playlist.Where(File.Exists));
        LoadJingleBanks();

        Browser.Navigate(_settings.BrowseFolder is { } folder && Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        _initialized = true;
        SelectedOutput = Pick(OutputDevices, _settings.OutputDeviceId, _devices.DefaultOutputId);
        LoadMicrophones();
        InputToAdd = InputDevices.FirstOrDefault(d => _microphoneStrips.All(m => m.DeviceId != d.Id));
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

    [RelayCommand(CanExecute = nameof(CanAddMicrophone))]
    private void AddMicrophone()
    {
        if (InputToAdd is { } device) AddMicrophone(device, new MicrophoneSettings { DeviceId = device.Id });
        InputToAdd = null;
    }

    private bool CanAddMicrophone() => IsIdle && InputToAdd is { } device && _microphoneStrips.All(m => m.DeviceId != device.Id);

    private void AddMicrophone(AudioDevice device, MicrophoneSettings saved)
    {
        try
        {
            var microphone = new MicrophoneSource(_devices.GetDevice(device.Id));
            microphone.Stopped += (_, ex) =>
            {
                if (ex is not null) _ui.Post(_ => StatusMessage = $"Mikrofon durdu ({device.Name}): {ex.Message}", null);
            };
            var channel = _engine.AddMicrophone(microphone, NextMicrophoneName());
            var strip = new ChannelStripViewModel(channel, device.Name, RemoveMicrophone)
            {
                DeviceId = device.Id,
                Volume = saved.Volume,
                MonitorEnabled = saved.MonitorEnabled,
            };
            _microphoneStrips.Add(strip);
            Channels.Insert(_microphoneStrips.Count - 1, strip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mikrofon açılamadı ({device.Name}): {ex.Message}";
        }

        AddMicrophoneCommand.NotifyCanExecuteChanged();
    }

    private void RemoveMicrophone(ChannelStripViewModel strip)
    {
        if (!IsIdle)
        {
            StatusMessage = "Kayıt sürerken mikrofon kaldırılamaz.";
            return;
        }

        _engine.RemoveMicrophone(strip.Channel);
        _microphoneStrips.Remove(strip);
        Channels.Remove(strip);
        AddMicrophoneCommand.NotifyCanExecuteChanged();
    }

    private string NextMicrophoneName()
    {
        for (int n = 1; ; n++)
        {
            var name = $"Mikrofon {n}";
            if (_microphoneStrips.All(m => m.Name != name)) return name;
        }
    }

    private void LoadMicrophones()
    {
        var saved = _settings.Microphones;
        if (saved.Count == 0)
        {
            // Settings from the first versions had one microphone; a fresh install uses the default device.
            var id = _settings.InputDeviceId ?? _devices.DefaultInputId;
            if (id is not null) saved = [new MicrophoneSettings { DeviceId = id, Volume = _settings.MicrophoneVolume }];
        }

        foreach (var microphone in saved)
        {
            if (InputDevices.FirstOrDefault(d => d.Id == microphone.DeviceId) is { } device)
            {
                AddMicrophone(device, microphone);
            }
        }
    }

    partial void OnMasterVolumeChanged(double value) => _engine.Mixer.MasterVolume = (float)value;

    partial void OnMonitorVolumeChanged(double value) => _engine.Mixer.MonitorVolume = (float)value;

    partial void OnIsMicrophoneLiveChanged(bool value) => _engine.IsMicrophoneLive = value;

    partial void OnDuckingEnabledChanged(bool value) => _engine.DuckingEnabled = value;

    partial void OnDuckLevelDbChanged(double value) => _engine.DuckLevelDb = value;

    [RelayCommand]
    private void ToggleMicrophone() => IsMicrophoneLive = !IsMicrophoneLive;

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
            var name = $"podcast-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            var encoder = SelectedEncoder.Create();
            var mix = new RecordingTarget(Path.Combine(RecordingFolder, name + encoder.FileExtension), encoder);
            var stems = RecordStems ? StemTargets(Path.Combine(RecordingFolder, name + " izler")) : null;
            _engine.Recorder.Start(mix, stems);
            StatusMessage = $"Kaydediliyor: {mix.Path}" + (stems is null ? "" : $" (+{stems.Count} ayrı iz)");
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

    /// <summary>One file per mixer channel, named after the channel, in its own folder.</summary>
    private Dictionary<MixerChannel, RecordingTarget> StemTargets(string folder)
    {
        Directory.CreateDirectory(folder);
        var invalid = Path.GetInvalidFileNameChars();
        return _engine.Mixer.Channels.ToDictionary(
            channel => channel,
            channel =>
            {
                var encoder = SelectedEncoder.Create();
                var fileName = new string(channel.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
                return new RecordingTarget(Path.Combine(folder, fileName + encoder.FileExtension), encoder);
            });
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

    private void LoadJingleBanks()
    {
        var banks = _settings.JingleBanks;
        if (banks.Count == 0)
        {
            // Settings from the first version had a single 4x4 grid.
            banks = [new JingleBankSettings { Name = "Genel", Slots = _settings.Jingles ?? [] }];
        }

        foreach (var saved in banks)
        {
            var bank = Jingles.AddBank(saved.Name, saved.Rows, saved.Columns);
            foreach (var slot in saved.Slots)
            {
                if (slot.Index >= 0 && slot.Index < bank.Cells.Count && File.Exists(slot.FilePath))
                {
                    bank.Cells[slot.Index].Assign(slot.FilePath);
                }
            }
        }
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
        _settings.InputDeviceId = null;
        _settings.Microphones = _microphoneStrips
            .Select(m => new MicrophoneSettings { DeviceId = m.DeviceId!, Volume = m.Volume, MonitorEnabled = m.MonitorEnabled })
            .ToList();
        _settings.RecordStems = RecordStems;
        _settings.OutputDeviceId = SelectedOutput?.Id;
        _settings.EncoderId = SelectedEncoder.Id;
        _settings.RecordingFolder = RecordingFolder;
        _settings.BrowseFolder = Browser.CurrentFolder;
        _settings.MusicVolume = _engine.MusicChannel.Volume;
        _settings.JingleVolume = _engine.JingleChannel.Volume;
        _settings.MasterVolume = MasterVolume;
        _settings.MonitorVolume = MonitorVolume;
        _settings.AutoAdvance = Playlist.AutoAdvance;
        _settings.CrossfadeSeconds = Playlist.CrossfadeSeconds;
        _settings.DuckingEnabled = DuckingEnabled;
        _settings.DuckLevelDb = DuckLevelDb;
        _settings.Playlist = Playlist.Items.Select(i => i.FilePath).ToList();
        _settings.Jingles = null;
        _settings.JingleBanks = Jingles.Banks
            .Select(b => new JingleBankSettings
            {
                Name = b.Name,
                Rows = b.Rows,
                Columns = b.Columns,
                Slots = b.Cells
                    .Where(c => c.FilePath is not null)
                    .Select(c => new JingleSlot { Index = c.Index, FilePath = c.FilePath! })
                    .ToList(),
            })
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
