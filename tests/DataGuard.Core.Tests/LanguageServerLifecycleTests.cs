using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class LanguageServerLifecycleTests
{
    private static string LanguageServerDllPath { get; } = Path.Combine(AppContext.BaseDirectory, "DataGuard.LanguageServer.dll");

    [Fact]
    public async Task Lifecycle_ShutdownReturnsNull_UnsupportedRequestReturnsMethodNotFound_ExitAfterShutdownExitsZero()
    {
        using var process = StartServer();
        try
        {
            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { capabilities = new { } },
            });
            using var initReply = await ReadJsonRpcAsync(process.StandardOutput.BaseStream);
            initReply.RootElement.GetProperty("id").GetInt32().Should().Be(1);

            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                method = "initialized",
                @params = new { },
            });

            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/hover",
                @params = new { },
            });
            using var hoverReply = await ReadJsonRpcAsync(process.StandardOutput.BaseStream);
            hoverReply.RootElement.GetProperty("id").GetInt32().Should().Be(2);
            hoverReply.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);

            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "shutdown",
            });
            using var shutdownReply = await ReadJsonRpcAsync(process.StandardOutput.BaseStream);
            shutdownReply.RootElement.GetProperty("id").GetInt32().Should().Be(3);
            shutdownReply.RootElement.TryGetProperty("result", out var resultProp).Should().BeTrue();
            resultProp.ValueKind.Should().Be(JsonValueKind.Null);

            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                method = "exit",
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cts.Token);
            process.ExitCode.Should().Be(0);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task Lifecycle_ExitWithoutShutdown_ExitsWithCodeOne()
    {
        using var process = StartServer();
        try
        {
            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                method = "exit",
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cts.Token);
            process.ExitCode.Should().Be(1);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Theory]
    [InlineData("file:///query.sql", "SELECT * FROM Customers", "DG017")]
    [InlineData("file:///query.cs", "var sql = \"SELECT * FROM Customers\";", "DG017")]
    public async Task DocumentOpen_PublishesSelectStarDiagnostic_AndCloseClearsIt(string uri, string text, string code)
    {
        using var process = StartServer();
        try
        {
            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                method = "textDocument/didOpen",
                @params = new { textDocument = new { uri, text, version = 1 } },
            });
            using var reply = await ReadJsonRpcAsync(process.StandardOutput.BaseStream);
            reply.RootElement.GetProperty("method").GetString().Should().Be("textDocument/publishDiagnostics");
            var parameters = reply.RootElement.GetProperty("params");
            parameters.GetProperty("uri").GetString().Should().Be(uri);
            var diagnostic = parameters.GetProperty("diagnostics").EnumerateArray().Single();
            diagnostic.GetProperty("code").GetString().Should().Be(code);
            diagnostic.GetProperty("severity").GetInt32().Should().Be(2);
            diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(0);

            await SendJsonRpcAsync(process.StandardInput.BaseStream, new
            {
                jsonrpc = "2.0",
                method = "textDocument/didClose",
                @params = new { textDocument = new { uri } },
            });
            using var cleared = await ReadJsonRpcAsync(process.StandardOutput.BaseStream);
            cleared.RootElement.GetProperty("params").GetProperty("diagnostics").GetArrayLength().Should().Be(0);
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().Be(0);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartServer()
    {
        File.Exists(LanguageServerDllPath).Should().BeTrue($"LanguageServer DLL must exist at {LanguageServerDllPath}");
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = CliProcessTestRunner.RepoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(LanguageServerDllPath);
        psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        return Process.Start(psi)!;
    }

    private static async Task SendJsonRpcAsync(Stream stdin, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await stdin.WriteAsync(header);
        await stdin.WriteAsync(bytes);
        await stdin.FlushAsync();
    }

    private static async Task<JsonDocument> ReadJsonRpcAsync(Stream stdout)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var headerBytes = new List<byte>();
        var single = new byte[1];
        while (true)
        {
            var read = await stdout.ReadAsync(single.AsMemory(0, 1), cts.Token);
            read.Should().Be(1, "expected JSON-RPC header from LanguageServer");
            headerBytes.Add(single[0]);
            var n = headerBytes.Count;
            if (n >= 4 && headerBytes[n - 4] == '\r' && headerBytes[n - 3] == '\n' && headerBytes[n - 2] == '\r' && headerBytes[n - 1] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lengthLine = headerText.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var count = int.Parse(lengthLine.Substring("Content-Length:".Length).Trim());
        var payload = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var received = await stdout.ReadAsync(payload.AsMemory(offset, count - offset), cts.Token);
            received.Should().BeGreaterThan(0);
            offset += received;
        }

        return JsonDocument.Parse(payload);
    }
}
