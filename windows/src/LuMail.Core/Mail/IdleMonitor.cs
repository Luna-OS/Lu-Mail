using MailKit;
using MailKit.Net.Imap;
using LuMail.Core.Models;

namespace LuMail.Core.Mail;

/// <summary>Entprellt Ereignisse: feuert einmal, nachdem <see cref="Delay"/> lang nichts Neues kam.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly Action _action;
    private readonly Timer _timer;
    private int _disposed;

    public TimeSpan Delay { get; }

    public Debouncer(TimeSpan delay, Action action)
    {
        Delay = delay;
        _action = action;
        _timer = new Timer(_ => { if (Volatile.Read(ref _disposed) == 0) _action(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Trigger()
    {
        if (Volatile.Read(ref _disposed) == 0) _timer.Change(Delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _timer.Dispose();
    }
}

/// <summary>Wartezeiten beim Neuverbinden: 30 s, 60 s, 120 s … höchstens 10 min.</summary>
public static class ReconnectBackoff
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(10);

    public static TimeSpan Next(TimeSpan? previous) =>
        previous is null ? First : TimeSpan.FromTicks(Math.Min(previous.Value.Ticks * 2, Max.Ticks));
}

/// <summary>
/// Push für neue Mails (FUNKTIONEN §1): eigene, zweite IMAP-Verbindung, <c>SELECT INBOX</c> + <c>IDLE</c>.
/// EXISTS/EXPUNGE/FETCH → <see cref="InboxChanged"/> (2 s entprellt). Alle 25 min DONE + neu IDLE.
/// Bei Abbruch Neuverbindung nach 30 s / 60 s / 120 s … (max. 10 min). Ohne IDLE-Fähigkeit beendet sich der Monitor.
/// </summary>
public sealed class IdleMonitor : IAsyncDisposable
{
    public static readonly TimeSpan IdleRestart = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    private readonly Account _account;
    private readonly Provider _provider;
    private readonly IMailCredentialSource _credentials;
    private readonly TimeSpan _timeout;
    private readonly Debouncer _debouncer;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public IdleMonitor(Account account, Provider provider, IMailCredentialSource credentials, TimeSpan? timeout = null)
    {
        _account = account;
        _provider = provider;
        _credentials = credentials;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _debouncer = new Debouncer(DebounceDelay, () => InboxChanged?.Invoke(this, account.Id));
    }

    /// <summary>Posteingang hat sich geändert (Argument: Konto-Id). Wird auf einem Hintergrund-Thread ausgelöst.</summary>
    public event EventHandler<string>? InboxChanged;

    /// <summary>False, sobald feststeht, dass der Server kein IDLE kann.</summary>
    public bool IsSupported { get; private set; } = true;

    public string AccountId => _account.Id;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken stop)
    {
        TimeSpan? backoff = null;
        while (!stop.IsCancellationRequested)
        {
            ImapClient? client = null;
            try
            {
                var credential = await _credentials.GetCredentialAsync(_account, _provider, false, stop).ConfigureAwait(false);
                client = await MailConnector.ConnectImapAsync(_provider, _account.ImapFor(_provider), credential, _timeout, stop).ConfigureAwait(false);
                if (!client.Capabilities.HasFlag(ImapCapabilities.Idle))
                {
                    IsSupported = false;
                    return;
                }
                var inbox = client.Inbox ?? throw new MailServiceException("Kein Posteingang.");
                inbox.CountChanged += (_, _) => _debouncer.Trigger();
                inbox.MessageExpunged += (_, _) => _debouncer.Trigger();
                inbox.MessageFlagsChanged += (_, _) => _debouncer.Trigger();
                inbox.MessagesVanished += (_, _) => _debouncer.Trigger();
                await inbox.OpenAsync(FolderAccess.ReadOnly, stop).ConfigureAwait(false);
                backoff = null;

                while (!stop.IsCancellationRequested && client.IsConnected)
                {
                    using var done = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    done.CancelAfter(IdleRestart);
                    await client.IdleAsync(done.Token, stop).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Verbindungsabbruch oder Anmeldefehler → später erneut.
            }
            finally
            {
                if (client is not null)
                {
                    try { if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception) { }
                    client.Dispose();
                }
            }

            if (stop.IsCancellationRequested) break;
            backoff = ReconnectBackoff.Next(backoff);
            try { await Task.Delay(backoff.Value, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception) { }
        }
        _debouncer.Dispose();
        _stop.Dispose();
    }
}
