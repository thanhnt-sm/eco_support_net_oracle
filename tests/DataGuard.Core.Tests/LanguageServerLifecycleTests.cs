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
        await using var session = StartServer();
        var process = session.Process;
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { capabilities = new { } },
        });
        using var initReply = await ReadJsonRpcAsync(session, "initialize");
        initReply.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        initReply.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32().Should().Be(1);

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
        using var hoverReply = await ReadJsonRpcAsync(session, "hover");
        hoverReply.RootElement.GetProperty("id").GetInt32().Should().Be(2);
        hoverReply.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);

        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "shutdown",
        });
        using var shutdownReply = await ReadJsonRpcAsync(session, "shutdown");
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

    [Fact]
    public async Task Lifecycle_ExitWithoutShutdown_ExitsWithCodeOne()
    {
        await using var session = StartServer();
        var process = session.Process;
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "exit",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(cts.Token);
        process.ExitCode.Should().Be(1);
    }

    [Theory]
    [InlineData("file:///query.sql", "SELECT * FROM Customers", "DG017")]
    [InlineData("file:///query.cs", "var sql = \"SELECT * FROM Customers\";", "DG017")]
    public async Task DocumentOpen_PublishesSelectStarDiagnostic_AndCloseClearsIt(string uri, string text, string code)
    {
        await using var session = StartServer();
        var process = session.Process;
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri, text, version = 1 } },
        });
        using var reply = await ReadJsonRpcAsync(session, "didOpen");
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
        using var cleared = await ReadJsonRpcAsync(session, "didClose");
        cleared.RootElement.GetProperty("params").GetProperty("diagnostics").GetArrayLength().Should().Be(0);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.Should().Be(0);
    }

    [Theory]
    [InlineData("\r", 1)]
    [InlineData("\n", 1)]
    [InlineData("\r\n", 1)]
    [InlineData("\r\n\r\n", 2)]
    [InlineData("\n\r", 2)]
    [InlineData("\r\n\n\r", 3)]
    public async Task DocumentOpen_LineEndingsAndUtf16_ProduceExactTokenRange(string lineEndings, int line)
    {
        await using var session = StartServer();
        var process = session.Process;
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { capabilities = new { } },
        });
        using var initialized = await ReadJsonRpcAsync(session, "initialize");
        initialized.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        initialized.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32().Should().Be(1);
        var text = "/*first*/" + lineEndings + "\t/*😀*/ SELECT * FROM Items";
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = "file:///synthetic.sql", text, version = 1 } },
        });
        using var reply = await ReadJsonRpcAsync(session, "didOpen");
        var diagnostic = reply.RootElement.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Single();
        diagnostic.GetProperty("code").GetString().Should().Be("DG017");
        var range = diagnostic.GetProperty("range");
        range.GetProperty("start").GetProperty("line").GetInt32().Should().Be(line);
        range.GetProperty("end").GetProperty("line").GetInt32().Should().Be(line);

        // Tab is one UTF-16 unit and the supplementary emoji is two; SELECT is six.
        range.GetProperty("start").GetProperty("character").GetInt32().Should().Be(8);
        range.GetProperty("end").GetProperty("character").GetInt32().Should().Be(14);
    }

    [Fact]
    public async Task DocumentChange_EmptyChanges_PreservesPendingDiagnosticAndSession()
    {
        await using var session = StartServer();
        var process = session.Process;
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { capabilities = new { } },
        });
        using var initialized = await ReadJsonRpcAsync(session, "initialize");
        initialized.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        initialized.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32().Should().Be(1);
        const string uri = "file:///synthetic.sql";
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new { textDocument = new { uri, text = "SELECT * FROM Items", version = 1 } },
        });
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "textDocument/didChange",
            @params = new { textDocument = new { uri, version = 2 }, contentChanges = Array.Empty<object>() },
        });
        using var reply = await ReadJsonRpcAsync(session, "didOpen");
        var diagnostic = reply.RootElement.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Single();
        diagnostic.GetProperty("code").GetString().Should().Be("DG017");
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new { jsonrpc = "2.0", id = 7, method = "shutdown" });
        using var shutdownReply = await ReadJsonRpcAsync(session, "shutdown");
        shutdownReply.RootElement.GetProperty("id").GetInt32().Should().Be(7);
        shutdownReply.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
        await SendJsonRpcAsync(process.StandardInput.BaseStream, new { jsonrpc = "2.0", method = "exit" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.Should().Be(0);
    }

    private static LanguageServerTestProcess StartServer()
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
        return new LanguageServerTestProcess(Process.Start(psi)!);
    }

    private static async Task SendJsonRpcAsync(Stream stdin, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await stdin.WriteAsync(header);
        await stdin.WriteAsync(bytes);
        await stdin.FlushAsync();
    }

    private static Task<JsonDocument> ReadJsonRpcAsync(LanguageServerTestProcess session, string phase) =>
        JsonRpcTestReader.ReadAsync(session.Process.StandardOutput.BaseStream, phase, TimeSpan.FromSeconds(3), session.DescribeState);
}
