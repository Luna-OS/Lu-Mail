using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LuMail.Core.Update;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LuMail.App.Services;

public enum UpdateState
{
    Idle,
    Checking,
    /// <summary>Neue Version gefunden, noch nicht geladen (Modus „Nur benachrichtigen“).</summary>
    Available,
    Downloading,
    /// <summary>Geladen und geprüft – Installation per „Jetzt neu starten“ oder beim Beenden.</summary>
    Ready,
    Failed,
}

/// <summary>
/// Auto-Updater der Windows-App: Zeitplan, Modus (Automatisch / Nur benachrichtigen / Aus),
/// Download und Installation über das Inno-Setup (<c>/VERYSILENT … /RELAUNCH=1|0</c>).
/// Alle Methoden laufen auf dem UI-Thread; <see cref="StateChanged"/> wird dort ausgelöst.
/// </summary>
public sealed class UpdateManager
{
    private const string SilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    private readonly AppServices _s;
    private readonly UpdateConfig _config;
    private readonly UpdateService _service;
    private DispatcherQueueTimer? _timer;
    private bool _busy;
    private bool _installStarted;
    private bool _restartAfterDownload;

    public UpdateManager(AppServices services)
    {
        _s = services;
        _config = UpdateConfig.LoadEmbedded();

        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateManager).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        CurrentVersion = SemanticVersion.FromAssemblyVersion(informational, assembly.GetName().Version);
        PlatformKey = UpdatePlatforms.ForWindows(RuntimeInformation.ProcessArchitecture);

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _service = new UpdateService(http, _config, CurrentVersion, PlatformKey);
    }

    public SemanticVersion CurrentVersion { get; }
    public string PlatformKey { get; }

    /// <summary>Version 0.0.0 (oder unlesbar) = Entwickler-Build → keine automatische Prüfung.</summary>
    public bool IsDevBuild => CurrentVersion.IsZero;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateInfo? Update { get; private set; }
    public string? DownloadedPath { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Download-Fortschritt 0…1.</summary>
    public double Progress { get; private set; }
    /// <summary>True, wenn der Nutzer den Download angestoßen hat (Fortschritt anzeigen).</summary>
    public bool IsInteractive { get; private set; }

    public event EventHandler? StateChanged;

    private void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- Zeitplan

    /// <summary>Startet die automatische Prüfung (initialDelaySeconds, danach alle checkIntervalHours).</summary>
    public void Start(DispatcherQueue dispatcher)
    {
        if (IsDevBuild || _timer is not null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = _config.InitialDelay > TimeSpan.Zero ? _config.InitialDelay : TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private async void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Interval = _config.CheckInterval;
        if (_s.Settings.Updates == UpdateMode.Off) return;
        try
        {
            await CheckAsync(interactive: false);
        }
        catch (Exception)
        {
            // Automatische Prüfung: Fehler still ignorieren (nächster Versuch beim nächsten Intervall).
        }
    }

    // --------------------------------------------------------------- Prüfen

    /// <summary>„Jetzt nach Updates suchen“ – liefert einen deutschen Ergebnistext.</summary>
    public async Task<string> CheckNowAsync()
    {
        if (State == UpdateState.Ready && Update is not null) return $"Lu-Mail {Update.Version} ist bereit und wird beim Beenden installiert.";
        if (State == UpdateState.Downloading && Update is not null) return $"Lu-Mail {Update.Version} wird heruntergeladen …";
        try
        {
            var info = await CheckAsync(interactive: true);
            if (info is null) return $"Lu-Mail ist aktuell ({CurrentVersion}).";
            return State switch
            {
                UpdateState.Ready => $"Lu-Mail {info.Version} ist bereit.",
                UpdateState.Downloading => $"Lu-Mail {info.Version} wird heruntergeladen …",
                _ => $"Lu-Mail {info.Version} ist verfügbar.",
            };
        }
        catch (Exception ex)
        {
            return "Update-Prüfung fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task<UpdateInfo?> CheckAsync(bool interactive)
    {
        if (_busy) return Update;
        _busy = true;
        try
        {
            if (State is UpdateState.Idle or UpdateState.Failed) SetState(UpdateState.Checking);
            UpdateInfo? info;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            {
                info = await _service.CheckAsync(cts.Token);
            }
            if (info is null)
            {
                if (State == UpdateState.Checking) SetState(UpdateState.Idle);
                return null;
            }

            // Schon geladen?
            if (State == UpdateState.Ready && Update?.Version == info.Version) return info;

            Update = info;
            LastError = null;
            if (_s.Settings.Updates == UpdateMode.Automatic)
            {
                IsInteractive = interactive;
                await DownloadCoreAsync();
            }
            else
            {
                SetState(UpdateState.Available);
            }
            return info;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            if (State == UpdateState.Checking) SetState(interactive ? UpdateState.Failed : UpdateState.Idle);
            throw;
        }
        finally
        {
            _busy = false;
        }
    }

    // ------------------------------------------------------------- Download

    /// <summary>„Herunterladen und neu starten“ (Modus „Nur benachrichtigen“).</summary>
    public async Task DownloadAndRestartAsync()
    {
        if (Update is null || _busy) return;
        if (State == UpdateState.Ready && DownloadedPath is not null)
        {
            InstallAndRestart();
            return;
        }
        _busy = true;
        try
        {
            IsInteractive = true;
            _restartAfterDownload = true;
            await DownloadCoreAsync();
        }
        finally
        {
            _busy = false;
        }
        if (State == UpdateState.Ready && _restartAfterDownload) InstallAndRestart();
    }

    private async Task DownloadCoreAsync()
    {
        var update = Update!;
        Progress = 0;
        SetState(UpdateState.Downloading);
        var progress = new Progress<double>(p =>
        {
            Progress = p;
            if (IsInteractive) StateChanged?.Invoke(this, EventArgs.Empty);
        });
        try
        {
            DownloadedPath = await _service.DownloadAsync(update, progress);
            SetState(UpdateState.Ready);
        }
        catch (Exception ex)
        {
            DownloadedPath = null;
            LastError = ex.Message;
            _restartAfterDownload = false;
            SetState(UpdateState.Failed);
        }
    }

    // ----------------------------------------------------------- Installation

    /// <summary>„Jetzt neu starten“: Setup still starten (Neustart danach durch das Setup) und Lu-Mail beenden.</summary>
    public void InstallAndRestart()
    {
        if (!StartSetup(relaunch: true)) return;
        Application.Current.Exit();
    }

    /// <summary>Beim Schließen des Fensters: geladenes Update still installieren, ohne Neustart.</summary>
    public void InstallOnExitIfReady()
    {
        if (State == UpdateState.Ready) StartSetup(relaunch: false);
    }

    private bool StartSetup(bool relaunch)
    {
        if (_installStarted) return true;
        if (DownloadedPath is null || !File.Exists(DownloadedPath))
        {
            LastError = "Die Update-Datei wurde nicht gefunden.";
            SetState(UpdateState.Failed);
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(DownloadedPath)
            {
                Arguments = $"{SilentArgs} /RELAUNCH={(relaunch ? 1 : 0)}",
                UseShellExecute = true,
            });
            _installStarted = true;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LastError = "Das Update konnte nicht gestartet werden: " + ex.Message;
            SetState(UpdateState.Failed);
            return false;
        }
    }

    /// <summary>Release-Seite im Browser öffnen („Was ist neu?“).</summary>
    public void OpenReleaseNotes()
    {
        var url = Update?.HtmlUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) AppServices.OpenInBrowser(uri);
    }
}
