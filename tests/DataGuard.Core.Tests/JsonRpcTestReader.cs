using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DataGuard.Core.Tests;

internal static class JsonRpcTestReader
{
    private const int MaximumHeaderBytes = 8192;
    private const int MaximumPayloadBytes = 1_048_576;

    internal static async Task<JsonDocument> ReadAsync(Stream stdout, string phase, TimeSpan timeout, Func<string>? childState = null)
    {
        using var cts = new CancellationTokenSource(timeout);
        var started = Stopwatch.GetTimestamp();
        var headerBytes = new List<byte>();
        var payloadBytes = 0;

        string Details()
        {
            var child = "unavailable";
            try
            {
                child = childState?.Invoke() ?? child;
            }
            catch (Exception exception)
            {
                // Diagnostic collection must not mask the transport failure or print exception messages.
                child = $"snapshot-error:{exception.GetType().Name}";
            }

            return FormattableString.Invariant($"phase={phase} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} headerBytes={headerBytes.Count} payloadBytes={payloadBytes} child={child}");
        }

        try
        {
            var single = new byte[1];
            while (true)
            {
                var read = await stdout.ReadAsync(single.AsMemory(), cts.Token);
                if (read == 0)
                {
                    throw new EndOfStreamException($"LSP response ended inside its header; {Details()}");
                }

                headerBytes.Add(single[0]);
                if (headerBytes.Count > MaximumHeaderBytes)
                {
                    throw new InvalidDataException($"LSP response header exceeds its size limit; {Details()}");
                }

                var n = headerBytes.Count;
                if (n >= 4 && headerBytes[n - 4] == '\r' && headerBytes[n - 3] == '\n' && headerBytes[n - 2] == '\r' && headerBytes[n - 1] == '\n')
                {
                    break;
                }
            }

            var lengthLines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (lengthLines.Length != 1 ||
                !int.TryParse(lengthLines[0].Substring("Content-Length:".Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                count > MaximumPayloadBytes)
            {
                throw new InvalidDataException($"LSP response has invalid Content-Length; {Details()}");
            }

            var payload = new byte[count];
            while (payloadBytes < count)
            {
                var received = await stdout.ReadAsync(payload.AsMemory(payloadBytes, count - payloadBytes), cts.Token);
                if (received == 0)
                {
                    throw new EndOfStreamException($"LSP response ended inside its payload; {Details()}");
                }

                payloadBytes += received;
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                throw new InvalidDataException($"LSP response contains invalid JSON; {Details()}");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Do not attach an inner exception that could include transport-specific data.
            throw new TimeoutException($"LSP response-read budget expired; {Details()}");
        }
    }
}
