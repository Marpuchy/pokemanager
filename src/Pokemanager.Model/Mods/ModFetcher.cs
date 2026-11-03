using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Pokemanager.Model.Mods;

/// <summary>
/// One answer to one request: enough to tell the bytes of a file from a web page pretending to be one, and to put the
/// cookie back on the next request without a cookie jar getting involved.
/// </summary>
/// <param name="Content">The body, to be read once and disposed by the caller.</param>
/// <param name="Length">The length the server declared, or null when it declared none.</param>
/// <param name="ContentType">The declared media type, lower-cased, or null.</param>
/// <param name="FileName">The name from <c>Content-Disposition</c>, when there is one.</param>
/// <param name="Cookies">
/// The cookies the answer set, already folded into a <c>Cookie</c> header value. Drive's confirmation page sets one
/// and then expects it back, which is the whole reason this is here.
/// </param>
public sealed record ModFetchResponse(
    Stream Content,
    long? Length,
    string? ContentType,
    string? FileName,
    string? Cookies) : IDisposable
{
    /// <summary>
    /// Whether this looks like a web page rather than a file. Drive answers a large file with its confirmation page,
    /// and the only honest way to notice is that the body is HTML when a file was asked for.
    /// </summary>
    public bool LooksLikeHtml =>
        ContentType is not null
        && (ContentType.Contains("text/html", StringComparison.Ordinal)
            || ContentType.Contains("application/xhtml", StringComparison.Ordinal));

    public void Dispose() => Content.Dispose();
}

/// <summary>
/// The one way out to the network, so everything above it can be tested without one. A test hands over a fake that
/// answers from memory — no socket is opened in the test suite, and the Drive confirmation dance is exercised all the
/// same.
/// </summary>
public interface IModFetcher
{
    /// <summary>
    /// Asks for a URL and gives back the answer without reading its body. Throws <see cref="ModFetchException"/> for
    /// anything the chain should degrade on (a refusal, a missing asset, no network).
    /// </summary>
    /// <param name="url">What to ask for.</param>
    /// <param name="cookies">A <c>Cookie</c> header to send, or null.</param>
    Task<ModFetchResponse> GetAsync(Uri url, string? cookies, CancellationToken cancel);
}

/// <summary>A fetch that did not give a file, with a sentence that says what happened.</summary>
public sealed class ModFetchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The real fetcher: one <b>shared, static</b> <see cref="HttpClient"/> for the whole application, because a client
/// per request runs the machine out of sockets. No cookie container and no automatic decompression of the body we are
/// about to write to disk: the cookies travel by hand (Drive wants its own back) and a mod file is already compressed.
/// </summary>
/// <remarks>
/// There is no overall timeout: these are hundreds of megabytes, and 2.3 GB for the model archive, so a clock on the
/// whole operation would kill a download that is working. What stops it is the <see cref="CancellationToken"/> the
/// user holds. Only connecting is given a deadline, which is what distinguishes "no network" from "slow network".
/// </remarks>
public sealed class HttpModFetcher : IModFetcher
{
    /// <summary>Who we say we are. A plain, honest agent: this is the user's own download of a public file.</summary>
    public static readonly string UserAgent = "Pokemanager/1.0 (+https://github.com/pokemanager; mod downloader)";

    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            // The cookies are carried by hand through the chain: a shared jar would make one download's Drive cookie
            // leak into another's request, and the chain has to be able to say exactly what it sent.
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public async Task<ModFetchResponse> GetAsync(Uri url, string? cookies, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookies is { Length: > 0 })
            request.Headers.TryAddWithoutValidation("Cookie", cookies);

        HttpResponseMessage response;
        try
        {
            // Headers only: the body is streamed to disk, never buffered — the model archive would not fit in memory.
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new ModFetchException(ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            // Cancellation belongs to the user; a timeout of our own is a failure of the source.
            throw new ModFetchException(ex.Message, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            string reason = response.ReasonPhrase ?? response.StatusCode.ToString();
            response.Dispose();
            throw new ModFetchException($"HTTP {status} {reason}");
        }

        var content = response.Content;
        string? setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(FirstPair).Where(v => v.Length > 0))
            : null;

        var stream = await content.ReadAsStreamAsync(cancel);
        return new ModFetchResponse(
            new ResponseStream(stream, response),
            content.Headers.ContentLength,
            content.Headers.ContentType?.MediaType?.ToLowerInvariant(),
            FileNameOf(content.Headers.ContentDisposition),
            setCookies is { Length: > 0 } ? setCookies : null);
    }

    /// <summary><c>name=value; Path=/; HttpOnly</c> → <c>name=value</c>: what goes back in a <c>Cookie</c> header.</summary>
    private static string FirstPair(string setCookie)
    {
        string pair = setCookie.Split(';', 2)[0].Trim();
        return pair.Contains('=', StringComparison.Ordinal) ? pair : "";
    }

    private static string? FileNameOf(ContentDispositionHeaderValue? disposition)
    {
        string? name = disposition?.FileNameStar ?? disposition?.FileName;
        return name?.Trim('"') is { Length: > 0 } trimmed ? trimmed : null;
    }

    /// <summary>Keeps the response alive for as long as its body is being read, and disposes both together.</summary>
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) => inner.ReadAsync(buffer, cancel);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) => inner.ReadAsync(buffer, offset, count, cancel);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
