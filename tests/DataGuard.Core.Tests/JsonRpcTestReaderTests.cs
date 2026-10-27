using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class JsonRpcTestReaderTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task ReadAsync_FragmentedUtf8Frame_PreservesPayloadAndNextFrame(int fragmentSize)
    {
        const string firstPayload = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"Tiếng Việt 😀\"}";
        const string secondPayload = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":null}";
        var firstFrame = Frame(firstPayload);
        using var stream = new FragmentedReadStream(firstFrame.Concat(Frame(secondPayload)).ToArray(), fragmentSize);

        using var first = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout);

        first.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        first.RootElement.GetProperty("result").GetString().Should().Be("Tiếng Việt 😀");
        stream.BytesRead.Should().Be(firstFrame.Length, "one response must not consume the next frame");

        using var second = await JsonRpcTestReader.ReadAsync(stream, "shutdown", ReadTimeout);

        second.RootElement.GetProperty("id").GetInt32().Should().Be(2);
        second.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
        stream.BytesRead.Should().Be(firstFrame.Length + Frame(secondPayload).Length);
        stream.IsDisposed.Should().BeFalse("the caller owns the transport stream");
    }

    [Fact]
    public async Task ReadAsync_UsesOneCancellableTokenAcrossHeaderAndPayload()
    {
        using var stream = new FragmentedReadStream(Frame("{\"id\":1,\"result\":null}"), fragmentSize: 2);

        using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout);

        response.RootElement.GetProperty("id").GetInt32().Should().Be(1);
        stream.ReadTokens.Should().HaveCountGreaterThan(2);
        var frameToken = stream.ReadTokens[0];
        frameToken.CanBeCanceled.Should().BeTrue();
        stream.ReadTokens.Should().OnlyContain(
            token => token == frameToken,
            "fragmented header and payload reads must share the frame's cancellation budget");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Content-Length: 2\r\n")]
    public async Task ReadAsync_EndOfStreamBeforeHeaderCompletes_Throws(string prefix)
    {
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(prefix), fragmentSize: 2);

        var read = async () => { using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout); };

        await read.Should().ThrowAsync<EndOfStreamException>();
        stream.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_EndOfStreamInsidePayload_Throws()
    {
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes("Content-Length: 10\r\n\r\n{}"), fragmentSize: 3);

        var read = async () => { using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout); };

        await read.Should().ThrowAsync<EndOfStreamException>();
        stream.IsDisposed.Should().BeFalse();
    }

    [Theory]
    [InlineData("Content-Length: invalid\r\n\r\n")]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 1048577\r\n\r\n")]
    [InlineData("Content-Type: application/json\r\n\r\n")]
    public async Task ReadAsync_InvalidContentLength_RejectsBeforeReadingPayload(string header)
    {
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(header), fragmentSize: 3);

        var read = async () => { using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout); };

        await read.Should().ThrowAsync<InvalidDataException>();
        stream.BytesRead.Should().Be(Encoding.ASCII.GetByteCount(header));
        stream.IsDisposed.Should().BeFalse();
    }

    [Theory]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n")]
    [InlineData("Content-Length: 2\r\nContent-Length: 3\r\n\r\n")]
    public async Task ReadAsync_DuplicateContentLength_RejectsAmbiguousFrame(string header)
    {
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(header), fragmentSize: 3);

        var read = async () => { using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout); };

        await read.Should().ThrowAsync<InvalidDataException>();
        stream.BytesRead.Should().Be(Encoding.ASCII.GetByteCount(header));
    }

    [Fact]
    public async Task ReadAsync_HeaderAboveEightKiB_Rejects()
    {
        var header = "Content-Length: 2\r\nX-Padding: " + new string('a', 8192) + "\r\n\r\n";
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(header), fragmentSize: 17);

        var read = async () => { using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout); };

        await read.Should().ThrowAsync<InvalidDataException>();
        stream.BytesRead.Should().BeLessThanOrEqualTo(8193);
    }

    [Fact]
    public async Task ReadAsync_HeaderAtEightKiBLimit_Accepts()
    {
        const string prefix = "Content-Length: 2\r\nX-Padding: ";
        var header = prefix + new string('a', 8192 - Encoding.ASCII.GetByteCount(prefix) - 4) + "\r\n\r\n";
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(header + "{}"), fragmentSize: 17);

        using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout);

        response.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        stream.BytesRead.Should().Be(8194);
    }

    [Fact]
    public async Task ReadAsync_PayloadAtOneMiBLimit_Accepts()
    {
        const int maximumPayloadBytes = 1_048_576;
        var content = new string('a', maximumPayloadBytes - Encoding.UTF8.GetByteCount("{\"data\":\"\"}"));
        using var stream = new FragmentedReadStream(Frame("{\"data\":\"" + content + "\"}"), fragmentSize: 4096);

        using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", ReadTimeout);

        response.RootElement.GetProperty("data").GetString().Should().HaveLength(content.Length);
    }

    [Fact]
    public async Task ReadAsync_CancellationInsideHeader_ReportsPhaseAndProgressWithoutHeaderText()
    {
        const string prefix = "Content-Length: do-not-log-header\r\n";
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(prefix), fragmentSize: 3, stallAtEnd: true);

        var read = async () =>
        {
            using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", TimeSpan.FromMilliseconds(250), () => "running");
        };

        var failure = (await read.Should().ThrowAsync<TimeoutException>()).Which;

        failure.Message.Should().Contain("phase=initialize")
            .And.Contain($"headerBytes={Encoding.ASCII.GetByteCount(prefix)}")
            .And.Contain("payloadBytes=0")
            .And.Contain("child=running")
            .And.NotContain("do-not-log-header");
        stream.BytesRead.Should().Be(Encoding.ASCII.GetByteCount(prefix));
        stream.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_CancellationInsidePayload_ReportsPhaseAndProgressWithoutPayloadText()
    {
        const string header = "Content-Length: 128\r\n\r\n";
        const string payloadPrefix = "{\"secret\":\"do-not-log-payload\"";
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(header + payloadPrefix), fragmentSize: 3, stallAtEnd: true);

        var read = async () =>
        {
            using var response = await JsonRpcTestReader.ReadAsync(stream, "initialize", TimeSpan.FromMilliseconds(250), () => "running");
        };

        var failure = (await read.Should().ThrowAsync<TimeoutException>()).Which;

        failure.Message.Should().Contain("phase=initialize")
            .And.Contain($"headerBytes={Encoding.ASCII.GetByteCount(header)}")
            .And.Contain($"payloadBytes={Encoding.ASCII.GetByteCount(payloadPrefix)}")
            .And.Contain("child=running")
            .And.NotContain("do-not-log-payload");
        stream.IsDisposed.Should().BeFalse();
        var frameToken = stream.ReadTokens[0];
        stream.ReadTokens.Should().OnlyContain(token => token == frameToken);
    }

    [Fact]
    public async Task ReadAsync_ThrowingChildState_PreservesTimeoutWithoutRetainingExceptionText()
    {
        const string prefix = "Content-Length: 2\r\n";
        using var stream = new FragmentedReadStream(Encoding.ASCII.GetBytes(prefix), fragmentSize: 3, stallAtEnd: true);

        var read = async () =>
        {
            using var response = await JsonRpcTestReader.ReadAsync(
                stream,
                "initialize",
                TimeSpan.FromMilliseconds(250),
                () => throw new InvalidOperationException("do-not-log-child-state"));
        };

        var failure = (await read.Should().ThrowAsync<TimeoutException>()).Which;

        failure.Message.Should().Contain("phase=initialize")
            .And.Contain($"headerBytes={Encoding.ASCII.GetByteCount(prefix)}")
            .And.Contain("payloadBytes=0")
            .And.Contain("child=")
            .And.Contain(nameof(InvalidOperationException))
            .And.NotContain("do-not-log-child-state");
        failure.InnerException.Should().BeNull("diagnostics must not retain an unfiltered child-state exception");
    }

    private static byte[] Frame(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n").Concat(bytes).ToArray();
    }

    private sealed class FragmentedReadStream(byte[] prefix, int fragmentSize, bool stallAtEnd = false) : Stream
    {
        public List<CancellationToken> ReadTokens { get; } = new();

        public int BytesRead { get; private set; }

        public bool IsDisposed { get; private set; }

        public override bool CanRead => !IsDisposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => prefix.Length;

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ReadTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (BytesRead < prefix.Length)
            {
                return CopyAvailable(buffer.Span);
            }

            if (stallAtEnd)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).WaitAsync(TimeSpan.FromSeconds(5));
            }

            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (stallAtEnd && BytesRead == prefix.Length)
            {
                throw new NotSupportedException("The cancellation fixture requires asynchronous reads.");
            }

            return CopyAvailable(buffer.AsSpan(offset, count));
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        private int CopyAvailable(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, fragmentSize), prefix.Length - BytesRead);
            prefix.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return count;
        }
    }
}
