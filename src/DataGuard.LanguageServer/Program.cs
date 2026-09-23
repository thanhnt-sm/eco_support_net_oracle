using System.Text;
using System.Text.Json;
using DataGuard.SqlClassification;

const int MaximumMessageBytes = 1_048_576;
const int DiagnosticDebounceMilliseconds = 150;
var pendingDiagnostics = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
using var shutdown = new CancellationTokenSource();
var writeGate = new SemaphoreSlim(1, 1);

while (await ReadMessageAsync(Console.OpenStandardInput()) is { } message)
{
    using var document = JsonDocument.Parse(message);
    var root = document.RootElement;
    if (!root.TryGetProperty("method", out var method))
    {
        continue;
    }
    if (method.GetString() == "initialize")
    {
        await WriteAsync(new { jsonrpc = "2.0", id = root.GetProperty("id").Clone(), result = new { capabilities = new { textDocumentSync = 1 } } });
    }
    else if (method.GetString() is "textDocument/didOpen" or "textDocument/didChange")
    {
        var textDocument = root.GetProperty("params").GetProperty("textDocument");
        var uri = new Uri(textDocument.GetProperty("uri").GetString()!);
        var text = method.GetString() == "textDocument/didOpen"
            ? textDocument.GetProperty("text").GetString() ?? string.Empty
            : root.GetProperty("params").GetProperty("contentChanges").EnumerateArray().Last().GetProperty("text").GetString() ?? string.Empty;
        var version = textDocument.TryGetProperty("version", out var versionValue) ? versionValue.ToString() : string.Empty;
        var key = uri.AbsoluteUri;
        if (pendingDiagnostics.Remove(key, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var pending = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        pendingDiagnostics[key] = pending;
        _ = PublishDebouncedAsync(uri, text, version, pending);
    }
    else if (method.GetString() == "textDocument/didClose")
    {
        var uri = new Uri(root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString()!);
        var key = uri.AbsoluteUri;
        if (pendingDiagnostics.Remove(key, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        await WriteAsync(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri = uri.AbsoluteUri, diagnostics = Array.Empty<object>() } });
    }
}

async Task PublishDebouncedAsync(Uri uri, string text, string version, CancellationTokenSource pending)
{
    try
    {
        await Task.Delay(DiagnosticDebounceMilliseconds, pending.Token);
        var diagnostics = SqlClassifier.Classify(text, uri, version)
            .Select(item => new
            {
                range = new { start = Position(text, item.Start), end = Position(text, item.Start + item.Length) },
                severity = item.IsSelectStar ? 2 : 3,
                code = item.IsSelectStar ? "DG017" : "DGSQL001",
                source = "DataGuard",
                message = item.DetailMessage ?? $"SQL {item.Kind} statement requires offline contract validation.",
            });
        await WriteAsync(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri = uri.AbsoluteUri, diagnostics } });
    }
    catch (OperationCanceledException) when (pending.IsCancellationRequested)
    {
        // A newer document version or shutdown superseded this publication.
    }
    finally
    {
        if (pendingDiagnostics.TryGetValue(uri.AbsoluteUri, out var current) && ReferenceEquals(current, pending))
        {
            pendingDiagnostics.Remove(uri.AbsoluteUri);
        }

        pending.Dispose();
    }
}

static object Position(string text, int offset)
{
    var line = 0;
    var character = 0;
    for (var index = 0; index < offset; index++)
    {
        if (text[index] == '\n')
        {
            line++;
            character = 0;
        }
        else
        {
            character++;
        }
    }
    return new { line, character };
}

static async Task<string?> ReadMessageAsync(Stream stream)
{
    var header = new List<byte>();
    var previous = -1;
    while (true)
    {
        var current = stream.ReadByte();
        if (current < 0)
        {
            return null;
        }

        header.Add((byte)current);
        if (header.Count > 8_192)
        {
            throw new InvalidDataException("Language Server message header exceeds the 8 KiB limit.");
        }

        if (previous == '\r' && current == '\n' && header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n')
        {
            break;
        }

        previous = current;
    }
    var length = Encoding.ASCII.GetString(header.ToArray()).Split('\n').Select(line => line.Trim()).First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    var count = int.Parse(length.Substring("Content-Length:".Length));
    if (count < 0 || count > MaximumMessageBytes)
    {
        throw new InvalidDataException("Language Server Content-Length exceeds the 1 MiB limit.");
    }

    var payload = new byte[count];
    for (var read = 0; read < count;)
    {
        var received = await stream.ReadAsync(payload, read, count - read);
        if (received == 0)
        {
            throw new EndOfStreamException("Language Server input ended before the declared Content-Length payload.");
        }

        read += received;
    }
    return Encoding.UTF8.GetString(payload);
}

async Task WriteAsync(object value)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
    await writeGate.WaitAsync();
    try
    {
        await Console.Out.WriteAsync($"Content-Length: {bytes.Length}\r\n\r\n");
        await Console.OpenStandardOutput().WriteAsync(bytes);
        await Console.Out.FlushAsync();
    }
    finally
    {
        writeGate.Release();
    }
}
