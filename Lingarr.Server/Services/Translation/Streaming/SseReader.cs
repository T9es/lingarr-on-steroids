using System.Runtime.CompilerServices;

namespace Lingarr.Server.Services.Translation.Streaming;

/// <summary>
/// Represents a single Server-Sent Event.
/// </summary>
/// <param name="EventType">Optional event type (Anthropic format), null for unnamed events.</param>
/// <param name="Data">The data payload, null if no data.</param>
public readonly record struct SseEvent(string? EventType, string? Data);

/// <summary>
/// Reads Server-Sent Events from a stream, yielding (eventType?, data?) tuples.
/// Handles both OpenAI format (data: {...}\n\n data: [DONE]) and
/// Anthropic format (event: content_block_delta\n data: {...}\n\n).
/// Messages are delimited by \n\n as per SSE spec.
/// </summary>
public class SseReader : IAsyncDisposable
{
    private readonly StreamReader _reader;
    private bool _disposed;

    public SseReader(Stream stream)
    {
        _reader = new StreamReader(stream);
    }

    /// <summary>
    /// Reads Server-Sent Events from the stream.
    /// Yields one <see cref="SseEvent"/> per message (delimited by blank line).
    /// </summary>
    public async IAsyncEnumerable<SseEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? currentEvent = null;
        var dataLines = new List<string>(2);

        while (await _reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                // Blank line = message delimiter (\n\n)
                if (dataLines.Count > 0)
                {
                    yield return new SseEvent(currentEvent, string.Join("\n", dataLines));
                }

                currentEvent = null;
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("event:"))
            {
                currentEvent = line.AsSpan(6).Trim().ToString();
            }
            else if (line.StartsWith("data:"))
            {
                // data: followed optionally by a space, then the content
                var data = line.AsSpan(5).Trim().ToString();
                if (data.Length > 0 || dataLines.Count > 0)
                {
                    dataLines.Add(data);
                }
            }
            // Lines starting with ':' are SSE comments — ignore
            // Other unrecognized lines are ignored per SSE spec
        }

        // Yield remaining data if stream ended without a trailing blank line
        if (dataLines.Count > 0)
        {
            yield return new SseEvent(currentEvent, string.Join("\n", dataLines));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _reader.Dispose();
        }
    }
}
