using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal sealed class WindowsLocalRtmpProofServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serverTask;
    private readonly object _gate = new();
    private readonly List<byte[]> _videoPackets = [];
    private TcpClient? _client;
    private Exception? _failure;
    private int _chunkSize = 128;

    public WindowsLocalRtmpProofServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        Url = $"rtmp://127.0.0.1:{endpoint.Port}/live/proof";
        _serverTask = Task.Run(RunAsync);
    }

    public string Url { get; }

    public int VideoPacketCount
    {
        get
        {
            lock (_gate)
                return _videoPackets.Count;
        }
    }

    public async ValueTask WaitForVideoPacketsAsync(
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailed();

            lock (_gate)
            {
                if (_videoPackets.Count >= count)
                    return;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        ThrowIfFailed();
        throw new TimeoutException($"RTMP proof server did not receive {count} video packet(s) within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        _client?.Dispose();

        try
        {
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            await using var stream = _client.GetStream();
            await PerformHandshakeAsync(stream, _stop.Token).ConfigureAwait(false);

            while (!_stop.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(stream, _stop.Token).ConfigureAwait(false);
                if (message is null)
                    return;

                if (message.Value.MessageTypeId == 1 && message.Value.Payload.Length == 4)
                {
                    _chunkSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(message.Value.Payload));
                }
                else if (message.Value.MessageTypeId == 20)
                {
                    if (PayloadContains(message.Value.Payload, "connect"))
                    {
                        await WriteCommandResponseAsync(
                                stream,
                                transactionId: 1,
                                numericResult: null,
                                _stop.Token)
                            .ConfigureAwait(false);
                    }
                    else if (PayloadContains(message.Value.Payload, "createStream"))
                    {
                        await WriteCommandResponseAsync(
                                stream,
                                transactionId: 2,
                                numericResult: 1,
                                _stop.Token)
                            .ConfigureAwait(false);
                    }
                }
                else if (message.Value.MessageTypeId == 9)
                {
                    lock (_gate)
                        _videoPackets.Add(message.Value.Payload);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _failure = ex;
        }
    }

    private static async ValueTask PerformHandshakeAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var c0c1 = new byte[1 + 1536];
        await stream.ReadExactlyAsync(c0c1, cancellationToken).ConfigureAwait(false);
        if (c0c1[0] != 3)
            throw new InvalidOperationException($"RTMP client used unsupported handshake version {c0c1[0]}.");

        var s0s1s2 = new byte[1 + 1536 + 1536];
        s0s1s2[0] = 3;
        BinaryPrimitives.WriteUInt32BigEndian(s0s1s2.AsSpan(1, 4), (uint)Environment.TickCount);
        c0c1.AsSpan(1, 1536).CopyTo(s0s1s2.AsSpan(1 + 1536));
        await stream.WriteAsync(s0s1s2, cancellationToken).ConfigureAwait(false);

        var c2 = new byte[1536];
        await stream.ReadExactlyAsync(c2, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RtmpMessage?> ReadMessageAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var firstByte = new byte[1];
        var bytesRead = await stream.ReadAsync(firstByte, cancellationToken).ConfigureAwait(false);
        if (bytesRead == 0)
            return null;

        var fmt = firstByte[0] >> 6;
        if (fmt != 0)
            throw new InvalidOperationException($"RTMP proof server expected full chunk headers only, got fmt={fmt}.");

        var header = new byte[11];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var timestamp = ReadUInt24BigEndian(header.AsSpan(0, 3));
        var length = checked((int)ReadUInt24BigEndian(header.AsSpan(3, 3)));
        var messageTypeId = header[6];
        if (timestamp == 0xFFFFFF)
        {
            var extendedTimestamp = new byte[4];
            await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
        }

        var payload = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var count = Math.Min(_chunkSize, length - offset);
            await stream.ReadExactlyAsync(payload.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;

            if (offset < length)
            {
                var continuationHeader = new byte[1];
                await stream.ReadExactlyAsync(continuationHeader, cancellationToken).ConfigureAwait(false);
                if (timestamp == 0xFFFFFF)
                {
                    var extendedTimestamp = new byte[4];
                    await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new RtmpMessage(messageTypeId, payload);
    }

    private static async ValueTask WriteCommandResponseAsync(
        NetworkStream stream,
        double transactionId,
        double? numericResult,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        WriteAmfString(payload, "_result");
        WriteAmfNumber(payload, transactionId);
        WriteAmfNull(payload);
        if (numericResult.HasValue)
            WriteAmfNumber(payload, numericResult.Value);
        else
            WriteAmfNull(payload);

        var data = payload.ToArray();
        var header = new byte[12];
        header[0] = 3;
        WriteUInt24BigEndian(header.AsSpan(1, 3), 0);
        WriteUInt24BigEndian(header.AsSpan(4, 3), (uint)data.Length);
        header[7] = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 0);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    private static bool PayloadContains(byte[] payload, string value) =>
        Encoding.UTF8.GetString(payload).Contains(value, StringComparison.Ordinal);

    private void ThrowIfFailed()
    {
        if (_failure is not null)
            throw new InvalidOperationException("Local RTMP proof server failed.", _failure);
    }

    private static void WriteAmfString(Stream output, string value)
    {
        output.WriteByte(0x02);
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        output.Write(length);
        output.Write(bytes);
    }

    private static void WriteAmfNumber(Stream output, double value)
    {
        output.WriteByte(0x00);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        output.Write(bytes);
    }

    private static void WriteAmfNull(Stream output) => output.WriteByte(0x05);

    private static uint ReadUInt24BigEndian(ReadOnlySpan<byte> source) =>
        ((uint)source[0] << 16) | ((uint)source[1] << 8) | source[2];

    private static void WriteUInt24BigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 16);
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)value;
    }

    private readonly record struct RtmpMessage(byte MessageTypeId, byte[] Payload);
}
