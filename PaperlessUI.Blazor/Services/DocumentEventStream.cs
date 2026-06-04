using System.Net.ServerSentEvents;
using Polly.Timeout;

namespace PaperlessUI.Blazor.Services;

/// <summary>
///     Consumes PaperlessREST's two SSE streams server-side with the .NET BCL <see cref="SseParser"/> —
///     the mirror of how SWEN3.Paperless.RabbitMq <em>produces</em> them via TypedResults.ServerSentEvents.
///     Scoped per Interactive Server circuit; raises <see cref="OnChanged"/> on every event so the page
///     can refetch + re-render. Reconnects with backoff on drop.
/// </summary>
public sealed class DocumentEventStream(IHttpClientFactory factory, ILogger<DocumentEventStream> logger)
    : IAsyncDisposable
{
    public event EventHandler? OnChanged;

    /// <summary>True while at least the OCR stream is connected; drives the "disconnected" banner.</summary>
    public bool Connected { get; private set; }

    private CancellationTokenSource? _cts;
    private Task _ocrTask = Task.CompletedTask;
    private Task _genAiTask = Task.CompletedTask;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        // Backoff intervals mirror the old wwwroot/app.js (5s OCR, 10s GenAI).
        // Tasks are stored and awaited in DisposeAsync so unhandled exceptions surface.
        _ocrTask   = ConsumeAsync("/api/v1/ocr-results",  TimeSpan.FromSeconds(5),  isPrimary: true,  _cts.Token);
        _genAiTask = ConsumeAsync("/api/v1/events/genai", TimeSpan.FromSeconds(10), isPrimary: false, _cts.Token);
    }

    private async Task ConsumeAsync(string path, TimeSpan retry, bool isPrimary, CancellationToken ct)
    {
        var client = factory.CreateClient("paperless");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                if (isPrimary) { Connected = true; OnChanged?.Invoke(this, EventArgs.Empty); }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var parser = SseParser.Create(stream);
                await foreach (SseItem<string> item in parser.EnumerateAsync(ct))
                {
                    logger.LogInformation("SSE {Event} on {Path}", item.EventType, path);
                    OnChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            // Only known transient transport faults are caught — each one specifically. Anything else
            // (bug, OOM, …) fails loud: it propagates out and surfaces via the awaited task in DisposeAsync.
            catch (HttpRequestException ex) { if (await ReconnectAfterAsync(ex)) break; }     // connection refused / 5xx
            catch (IOException ex) { if (await ReconnectAfterAsync(ex)) break; }              // mid-stream read failure
            catch (TimeoutRejectedException ex) { if (await ReconnectAfterAsync(ex)) break; } // resilience connect-timeout
        }

        return;

        // Signals disconnect, logs the transient fault, and backs off before the loop reconnects.
        // Returns true when the backoff wait itself was cancelled (caller should stop the loop).
        async Task<bool> ReconnectAfterAsync(Exception ex)
        {
            if (isPrimary) { Connected = false; OnChanged?.Invoke(this, EventArgs.Empty); }
            logger.LogWarning(ex, "SSE {Path} dropped; reconnecting in {Seconds}s", path, retry.TotalSeconds);
            try { await Task.Delay(retry, ct); return false; }
            catch (OperationCanceledException) { return true; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        await Task.WhenAll(_ocrTask, _genAiTask);
        _cts.Dispose();
        _cts = null;
    }
}
