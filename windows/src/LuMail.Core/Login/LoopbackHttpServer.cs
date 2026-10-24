using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LuMail.Core.Auth;

namespace LuMail.Core.Login;

/// <summary>Eine HTTP-Anfrage an den Loopback-Server.</summary>
public sealed class LoopbackRequest
{
    public string Method { get; init; } = "GET";
    /// <summary>Pfad ohne Query, z. B. "/login/abc".</summary>
    public string Path { get; init; } = "/";
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = "";

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    /// <summary>Formularfelder (application/x-www-form-urlencoded).</summary>
    public IReadOnlyDictionary<string, string> Form =>
        (Header("Content-Type") ?? "").StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            ? OAuthUrls.ParseQuery(Body)
            : new Dictionary<string, string>();
}

public sealed record LoopbackResponse(int StatusCode, string ContentType, string Body)
{
    public static LoopbackResponse Html(int status, string html) => new(status, "text/html; charset=utf-8", html);
    public static LoopbackResponse Text(int status, string text) => new(status, "text/plain; charset=utf-8", text);
}

/// <summary>
/// Einmaliger, minimaler HTTP/1.1-Server nur auf 127.0.0.1 und ::1 (gleicher, zufälliger Port).
/// Bewusst auf <see cref="TcpListener"/> statt HttpListener (http.sys) aufgebaut – keine URL-ACLs nötig.
/// Prüft den Host-Header (DNS-Rebinding-Schutz): nur <c>127.0.0.1:&lt;port&gt;</c> oder <c>localhost:&lt;port&gt;</c>.
/// </summary>
public sealed class LoopbackHttpServer : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    private readonly TcpListener _v4;
    private readonly TcpListener? _v6;
    private readonly Func<LoopbackRequest, CancellationToken, Task<LoopbackResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<Task> _inflight = new();
    private readonly object _gate = new();
    private int _disposed;

    public int Port { get; }
    public bool ListensOnIPv6 => _v6 is not null;
    public TimeSpan RequestReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    private LoopbackHttpServer(TcpListener v4, TcpListener? v6, int port,
        Func<LoopbackRequest, CancellationToken, Task<LoopbackResponse>> handler)
    {
        _v4 = v4;
        _v6 = v6;
        Port = port;
        _handler = handler;
    }

    /// <summary>Startet den Server auf einem zufälligen freien Port.</summary>
    public static LoopbackHttpServer Start(Func<LoopbackRequest, CancellationToken, Task<LoopbackResponse>> handler)
    {
        for (var attempt = 0; ; attempt++)
        {
            var v4 = new TcpListener(IPAddress.Loopback, 0);
            v4.Start();
            var port = ((IPEndPoint)v4.LocalEndpoint).Port;

            TcpListener? v6 = null;
            if (Socket.OSSupportsIPv6)
            {
                try
                {
                    v6 = new TcpListener(IPAddress.IPv6Loopback, port);
                    v6.Start();
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 5)
                {
                    // Port auf ::1 belegt → neuen Port versuchen.
                    v4.Stop();
                    continue;
                }
                catch (SocketException)
                {
                    v6 = null; // IPv6 nicht verfügbar – nur 127.0.0.1.
                }
            }

            var server = new LoopbackHttpServer(v4, v6, port, handler);
            server.Track(server.AcceptLoopAsync(v4));
            if (v6 is not null) server.Track(server.AcceptLoopAsync(v6));
            return server;
        }
    }

    /// <summary>Erlaubte Host-Header.</summary>
    public bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim();
        var port = Port.ToString(CultureInfo.InvariantCulture);
        return string.Equals(h, "127.0.0.1:" + port, StringComparison.OrdinalIgnoreCase)
               || string.Equals(h, "localhost:" + port, StringComparison.OrdinalIgnoreCase);
    }

    private void Track(Task task)
    {
        lock (_gate) _inflight.Add(task);
        task.ContinueWith(t =>
        {
            lock (_gate) _inflight.Remove(t);
        }, TaskScheduler.Default);
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                if (_cts.IsCancellationRequested) break;
                continue;
            }
            Track(HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                LoopbackResponse response;
                LoopbackRequest? request;

                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    readCts.CancelAfter(RequestReadTimeout);
                    try
                    {
                        request = await ReadRequestAsync(stream, readCts.Token).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        await WriteResponseAsync(stream, LoopbackResponse.Text(400, ex.Message)).ConfigureAwait(false);
                        return;
                    }
                }
                if (request is null) return;

                if (!IsAllowedHost(request.Header("Host")))
                {
                    response = LoopbackResponse.Text(403, "Forbidden: invalid Host header");
                }
                else
                {
                    try
                    {
                        response = await _handler(request, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        response = LoopbackResponse.Text(503, "Service Unavailable");
                    }
                    catch (Exception)
                    {
                        response = LoopbackResponse.Text(500, "Internal Server Error");
                    }
                }
                await WriteResponseAsync(stream, response).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Verbindung abgebrochen – ignorieren.
            }
        }
    }

    internal static async Task<LoopbackRequest?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var data = new MemoryStream();
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) return null;
            data.Write(buffer, 0, n);
            headerEnd = IndexOf(data.GetBuffer(), (int)data.Length, HeaderTerminator);
            if (headerEnd >= 0) break;
            if (data.Length > MaxHeaderBytes) throw new InvalidDataException("Header too large");
        }

        var raw = data.GetBuffer();
        var headerText = Encoding.Latin1.GetString(raw, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            throw new InvalidDataException("Bad request line");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var cl))
        {
            if (!int.TryParse(cl, NumberStyles.None, CultureInfo.InvariantCulture, out contentLength) || contentLength < 0)
                throw new InvalidDataException("Bad Content-Length");
            if (contentLength > MaxBodyBytes) throw new InvalidDataException("Body too large");
        }

        var bodyStart = headerEnd + HeaderTerminator.Length;
        var body = new byte[contentLength];
        var already = Math.Min(contentLength, (int)data.Length - bodyStart);
        Array.Copy(raw, bodyStart, body, 0, already);
        var read = already;
        while (read < contentLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, contentLength - read), ct).ConfigureAwait(false);
            if (n == 0) throw new InvalidDataException("Incomplete body");
            read += n;
        }

        var target = requestLine[1];
        var q = target.IndexOf('?');
        var path = q < 0 ? target : target[..q];
        var query = q < 0 ? "" : target[(q + 1)..];

        return new LoopbackRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = Uri.UnescapeDataString(path),
            Query = OAuthUrls.ParseQuery(query),
            Headers = headers,
            Body = Encoding.UTF8.GetString(body),
        };
    }

    private static int IndexOf(byte[] haystack, int length, byte[] needle)
    {
        for (var i = 0; i <= length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static async Task WriteResponseAsync(Stream stream, LoopbackResponse response)
    {
        var body = Encoding.UTF8.GetBytes(response.Body);
        var header = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(Reason(response.StatusCode)).Append("\r\n")
            .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token).ConfigureAwait(false);
        await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
        await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        410 => "Gone",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Status",
    };

    /// <summary>Stoppt die Listener und wartet kurz, bis laufende Antworten geschrieben sind.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _v4.Stop(); } catch (SocketException) { }
        try { _v6?.Stop(); } catch (SocketException) { }

        Task[] pending;
        lock (_gate) pending = _inflight.ToArray();
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Zeitüberschreitung oder Fehler in einer Verbindung – trotzdem beenden.
        }
        _cts.Cancel();
        _cts.Dispose();
    }
}
