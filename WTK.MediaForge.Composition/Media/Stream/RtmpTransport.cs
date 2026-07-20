using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WTK.MediaForge.Composition.Media.Stream;

internal interface IRtmpTransport : IDisposable
{
    string Url { get; }

    bool IsConnected { get; }

    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    ValueTask SendAsync(FlvPacket packet, CancellationToken cancellationToken = default);
}

internal sealed class TcpRtmpTransport : IRtmpTransport
{
    private const int RtmpVersion = 3;
    private const int HandshakePayloadSize = 1536;
    private const int DefaultPort = 1935;
    private const int OutboundChunkSize = 4096;
    private const byte MessageTypeSetChunkSize = 1;
    private const byte MessageTypeAmf3Command = 17;
    private const byte MessageTypeAmf0Command = 20;
    private const byte MessageTypeVideo = 9;

    private readonly RtmpEndpoint _endpoint;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _inboundChunkSize = 128;
    private uint _mediaStreamId = 1;
    private bool _connected;
    private bool _disposed;

    public TcpRtmpTransport(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _endpoint = RtmpEndpoint.Parse(url);
        Url = url;
    }

    public string Url { get; }

    public bool IsConnected => _connected;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connected)
            return;

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(_endpoint.Host, _endpoint.Port, cancellationToken).ConfigureAwait(false);
            _client = client;
            _stream = client.GetStream();

            await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);
            await WriteSetChunkSizeAsync(cancellationToken).ConfigureAwait(false);
            await WriteCommandAsync(
                    transactionId: 1,
                    messageStreamId: 0,
                    commandName: "connect",
                    commandArguments: BuildConnectCommandArguments(),
                    cancellationToken)
                .ConfigureAwait(false);
            _ = await WaitForAmfCommandResponseAsync(1, cancellationToken).ConfigureAwait(false);

            await WriteCommandAsync(
                    transactionId: 2,
                    messageStreamId: 0,
                    commandName: "createStream",
                    commandArguments: [AmfValue.Null()],
                    cancellationToken)
                .ConfigureAwait(false);
            var createStreamResponse = await WaitForAmfCommandResponseAsync(2, cancellationToken).ConfigureAwait(false);
            _mediaStreamId = createStreamResponse.NumericResult is > 0 and <= uint.MaxValue
                ? (uint)createStreamResponse.NumericResult.Value
                : throw new InvalidOperationException("RTMP createStream response did not return a valid media stream id.");

            await WriteCommandAsync(
                    transactionId: 0,
                    messageStreamId: _mediaStreamId,
                    commandName: "publish",
                    commandArguments:
                    [
                        AmfValue.Null(),
                        AmfValue.String(_endpoint.StreamName),
                        AmfValue.String("live")
                    ],
                    cancellationToken)
                .ConfigureAwait(false);

            _connected = true;
        }
        catch
        {
            client.Dispose();
            _client = null;
            _stream = null;
            _connected = false;
            throw;
        }
    }

    public async ValueTask SendAsync(FlvPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connected || _stream is null)
            throw new InvalidOperationException("RTMP transport is not connected.");

        await WriteRtmpMessageAsync(
                chunkStreamId: 6,
                messageTypeId: MessageTypeVideo,
                messageStreamId: _mediaStreamId,
                timestamp: ToRtmpTimestamp(packet.Timestamp),
                payload: packet.Data,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connected = false;
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    private async ValueTask PerformHandshakeAsync(CancellationToken cancellationToken)
    {
        var stream = GetConnectedStream();
        var c0c1 = new byte[1 + HandshakePayloadSize];
        c0c1[0] = RtmpVersion;
        BinaryPrimitives.WriteUInt32BigEndian(c0c1.AsSpan(1, 4), (uint)Environment.TickCount);
        RandomNumberGenerator.Fill(c0c1.AsSpan(9));

        await stream.WriteAsync(c0c1, cancellationToken).ConfigureAwait(false);

        var s0s1s2 = new byte[1 + HandshakePayloadSize + HandshakePayloadSize];
        await stream.ReadExactlyAsync(s0s1s2, cancellationToken).ConfigureAwait(false);
        if (s0s1s2[0] != RtmpVersion)
            throw new InvalidOperationException($"RTMP server returned unsupported handshake version {s0s1s2[0]}.");

        await stream.WriteAsync(s0s1s2.AsMemory(1, HandshakePayloadSize), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteSetChunkSizeAsync(CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, OutboundChunkSize);
        await WriteRtmpMessageAsync(2, MessageTypeSetChunkSize, 0, 0, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<AmfCommandResponse> WaitForAmfCommandResponseAsync(
        double transactionId,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            while (true)
            {
                var message = await ReadRtmpMessageAsync(linked.Token).ConfigureAwait(false);
                if (message.MessageTypeId == MessageTypeSetChunkSize && message.Payload.Length == 4)
                {
                    _inboundChunkSize = Math.Max(
                        1,
                        checked((int)BinaryPrimitives.ReadUInt32BigEndian(message.Payload.Span)));
                    continue;
                }

                if (message.MessageTypeId is not (MessageTypeAmf0Command or MessageTypeAmf3Command))
                    continue;

                var payload = message.MessageTypeId == MessageTypeAmf3Command && message.Payload.Length > 0
                    ? message.Payload[1..]
                    : message.Payload;
                if (TryReadAmfCommandResponse(payload.Span, out var response) &&
                    Math.Abs(response.TransactionId - transactionId) < double.Epsilon)
                {
                    return response;
                }
            }
        }
        catch (OperationCanceledException ex) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"RTMP server did not acknowledge transaction {transactionId} within 5 seconds.", ex);
        }
    }

    private async ValueTask WriteCommandAsync(
        double transactionId,
        uint messageStreamId,
        string commandName,
        IReadOnlyList<AmfValue> commandArguments,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        WriteAmfString(payload, commandName);
        WriteAmfNumber(payload, transactionId);
        foreach (var argument in commandArguments)
            WriteAmfValue(payload, argument);

        await WriteRtmpMessageAsync(
                chunkStreamId: 3,
                messageTypeId: MessageTypeAmf0Command,
                messageStreamId,
                timestamp: 0,
                payload: payload.ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask WriteRtmpMessageAsync(
        int chunkStreamId,
        byte messageTypeId,
        uint messageStreamId,
        uint timestamp,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var stream = GetConnectedStream();
        var offset = 0;
        var extendedTimestamp = timestamp >= 0xFFFFFF;
        var header = CreateMessageHeader(
            fmt: 0,
            chunkStreamId,
            timestamp,
            payload.Length,
            messageTypeId,
            messageStreamId);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        var chunkSize = Math.Min(OutboundChunkSize, payload.Length);
        if (chunkSize > 0)
            await stream.WriteAsync(payload.Slice(0, chunkSize), cancellationToken).ConfigureAwait(false);

        offset += chunkSize;
        while (offset < payload.Length)
        {
            var continuationHeader = CreateContinuationHeader(chunkStreamId, extendedTimestamp ? timestamp : null);
            await stream.WriteAsync(continuationHeader, cancellationToken).ConfigureAwait(false);

            chunkSize = Math.Min(OutboundChunkSize, payload.Length - offset);
            await stream.WriteAsync(payload.Slice(offset, chunkSize), cancellationToken).ConfigureAwait(false);
            offset += chunkSize;
        }
    }

    private async ValueTask<RtmpMessage> ReadRtmpMessageAsync(CancellationToken cancellationToken)
    {
        var stream = GetConnectedStream();
        var basicHeader = new byte[1];
        await stream.ReadExactlyAsync(basicHeader, cancellationToken).ConfigureAwait(false);

        var fmt = basicHeader[0] >> 6;
        if (fmt != 0)
            throw new NotSupportedException($"RTMP inbound chunk format {fmt} is not supported for command responses.");

        var header = new byte[11];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var timestamp = ReadUInt24BigEndian(header.AsSpan(0, 3));
        var payloadLength = checked((int)ReadUInt24BigEndian(header.AsSpan(3, 3)));
        var messageTypeId = header[6];
        if (timestamp == 0xFFFFFF)
        {
            var extendedTimestamp = new byte[4];
            await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
        }

        var payload = new byte[payloadLength];
        var offset = 0;
        while (offset < payloadLength)
        {
            var chunkLength = Math.Min(_inboundChunkSize, payloadLength - offset);
            await stream.ReadExactlyAsync(payload.AsMemory(offset, chunkLength), cancellationToken).ConfigureAwait(false);
            offset += chunkLength;

            if (offset < payloadLength)
            {
                await stream.ReadExactlyAsync(basicHeader, cancellationToken).ConfigureAwait(false);
                if (timestamp == 0xFFFFFF)
                {
                    var extendedTimestamp = new byte[4];
                    await stream.ReadExactlyAsync(extendedTimestamp, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new RtmpMessage(messageTypeId, payload);
    }

    private NetworkStream GetConnectedStream() =>
        _stream ?? throw new InvalidOperationException("RTMP transport is not connected.");

    private IReadOnlyList<AmfValue> BuildConnectCommandArguments() =>
    [
        AmfValue.Object(
        [
            new("app", AmfValue.String(_endpoint.AppName)),
            new("type", AmfValue.String("nonprivate")),
            new("flashVer", AmfValue.String("FMLE/3.0 (compatible; WTK MediaForge)")),
            new("tcUrl", AmfValue.String(_endpoint.TcUrl)),
            new("fpad", AmfValue.Boolean(false)),
            new("capabilities", AmfValue.Number(15)),
            new("audioCodecs", AmfValue.Number(0)),
            new("videoCodecs", AmfValue.Number(252)),
            new("videoFunction", AmfValue.Number(1)),
            new("objectEncoding", AmfValue.Number(0))
        ])
    ];

    private static byte[] CreateMessageHeader(
        int fmt,
        int chunkStreamId,
        uint timestamp,
        int payloadLength,
        byte messageTypeId,
        uint messageStreamId)
    {
        if (chunkStreamId is < 2 or > 63)
            throw new ArgumentOutOfRangeException(nameof(chunkStreamId), "Only one-byte RTMP chunk stream ids are supported.");

        var extendedTimestamp = timestamp >= 0xFFFFFF;
        var header = new byte[12 + (extendedTimestamp ? 4 : 0)];
        header[0] = (byte)((fmt << 6) | chunkStreamId);
        WriteUInt24BigEndian(header.AsSpan(1, 3), extendedTimestamp ? 0xFFFFFFu : timestamp);
        WriteUInt24BigEndian(header.AsSpan(4, 3), (uint)payloadLength);
        header[7] = messageTypeId;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), messageStreamId);
        if (extendedTimestamp)
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), timestamp);

        return header;
    }

    private static byte[] CreateContinuationHeader(
        int chunkStreamId,
        uint? extendedTimestamp)
    {
        if (chunkStreamId is < 2 or > 63)
            throw new ArgumentOutOfRangeException(nameof(chunkStreamId), "Only one-byte RTMP chunk stream ids are supported.");

        var header = new byte[1 + (extendedTimestamp.HasValue ? 4 : 0)];
        header[0] = (byte)((3 << 6) | chunkStreamId);
        if (extendedTimestamp.HasValue)
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), extendedTimestamp.Value);

        return header;
    }

    private static void WriteUInt24BigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 16);
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)value;
    }

    private static uint ReadUInt24BigEndian(ReadOnlySpan<byte> source) =>
        ((uint)source[0] << 16) | ((uint)source[1] << 8) | source[2];

    private static uint ToRtmpTimestamp(TimeSpan timestamp)
    {
        var milliseconds = Math.Max(0, timestamp.TotalMilliseconds);
        return milliseconds >= uint.MaxValue
            ? uint.MaxValue
            : (uint)milliseconds;
    }

    private static void WriteAmfValue(System.IO.Stream output, AmfValue value)
    {
        switch (value.Kind)
        {
            case AmfValueKind.Null:
                output.WriteByte(0x05);
                break;
            case AmfValueKind.String:
                WriteAmfString(output, value.StringValue!);
                break;
            case AmfValueKind.Number:
                WriteAmfNumber(output, value.NumberValue);
                break;
            case AmfValueKind.Boolean:
                output.WriteByte(0x01);
                output.WriteByte(value.BooleanValue ? (byte)1 : (byte)0);
                break;
            case AmfValueKind.Object:
                output.WriteByte(0x03);
                foreach (var property in value.ObjectProperties!)
                {
                    WriteAmfUtf8(output, property.Name, includeTypeMarker: false);
                    WriteAmfValue(output, property.Value);
                }

                output.WriteByte(0x00);
                output.WriteByte(0x00);
                output.WriteByte(0x09);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported AMF value kind.");
        }
    }

    private static void WriteAmfString(System.IO.Stream output, string value)
    {
        output.WriteByte(0x02);
        WriteAmfUtf8(output, value, includeTypeMarker: false);
    }

    private static void WriteAmfNumber(System.IO.Stream output, double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        output.WriteByte(0x00);
        output.Write(bytes);
    }

    private static void WriteAmfUtf8(System.IO.Stream output, string value, bool includeTypeMarker)
    {
        if (includeTypeMarker)
            output.WriteByte(0x02);

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "AMF0 string is too long.");

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        output.Write(length);
        output.Write(bytes);
    }

    private static bool TryReadAmfCommandResponse(
        ReadOnlySpan<byte> payload,
        out AmfCommandResponse response)
    {
        response = default;
        var reader = new AmfReader(payload);
        if (!reader.TryReadString(out var commandName) ||
            !reader.TryReadNumber(out var transactionId))
        {
            return false;
        }

        _ = reader.TrySkipValue();
        double? numericResult = null;
        if (reader.TryReadNumber(out var result))
            numericResult = result;

        response = new AmfCommandResponse(commandName, transactionId, numericResult);
        return string.Equals(commandName, "_result", StringComparison.Ordinal) ||
               string.Equals(commandName, "_error", StringComparison.Ordinal);
    }

    private sealed record RtmpEndpoint(
        string Host,
        int Port,
        string AppName,
        string StreamName,
        string TcUrl)
    {
        public static RtmpEndpoint Parse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "rtmp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentException("RTMP URL must use the form rtmp://host[:port]/app/stream.", nameof(url));
            }

            var segments = uri.AbsolutePath
                .Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2)
                throw new ArgumentException("RTMP URL must include both an app name and a stream name.", nameof(url));

            var appName = segments[0];
            var streamName = string.Join('/', segments.Skip(1));
            var port = uri.IsDefaultPort ? DefaultPort : uri.Port;
            var tcUrl = $"{uri.Scheme}://{uri.Host}:{port}/{appName}";
            return new RtmpEndpoint(uri.Host, port, appName, streamName, tcUrl);
        }
    }

    private readonly record struct RtmpMessage(byte MessageTypeId, ReadOnlyMemory<byte> Payload);

    private readonly record struct AmfCommandResponse(
        string CommandName,
        double TransactionId,
        double? NumericResult);

    private enum AmfValueKind
    {
        Null,
        String,
        Number,
        Boolean,
        Object
    }

    private sealed record AmfObjectProperty(string Name, AmfValue Value);

    private sealed record AmfValue(
        AmfValueKind Kind,
        string? StringValue = null,
        double NumberValue = 0,
        bool BooleanValue = false,
        IReadOnlyList<AmfObjectProperty>? ObjectProperties = null)
    {
        public static AmfValue Null() => new(AmfValueKind.Null);

        public static AmfValue String(string value) => new(AmfValueKind.String, StringValue: value);

        public static AmfValue Number(double value) => new(AmfValueKind.Number, NumberValue: value);

        public static AmfValue Boolean(bool value) => new(AmfValueKind.Boolean, BooleanValue: value);

        public static AmfValue Object(IReadOnlyList<AmfObjectProperty> properties) =>
            new(AmfValueKind.Object, ObjectProperties: properties);
    }

    private ref struct AmfReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public AmfReader(ReadOnlySpan<byte> data) => _data = data;

        public bool TryReadString(out string value)
        {
            value = string.Empty;
            if (!TryReadByte(out var marker) || marker != 0x02)
                return false;

            return TryReadUtf8(out value);
        }

        public bool TryReadNumber(out double value)
        {
            value = 0;
            if (!TryReadByte(out var marker) || marker != 0x00)
                return false;

            if (_offset + 8 > _data.Length)
                return false;

            var bits = BinaryPrimitives.ReadInt64BigEndian(_data.Slice(_offset, 8));
            _offset += 8;
            value = BitConverter.Int64BitsToDouble(bits);
            return true;
        }

        public bool TrySkipValue()
        {
            if (!TryReadByte(out var marker))
                return false;

            switch (marker)
            {
                case 0x00:
                    _offset += 8;
                    return _offset <= _data.Length;
                case 0x01:
                    _offset++;
                    return _offset <= _data.Length;
                case 0x02:
                    return TryReadUtf8(out _);
                case 0x03:
                    return TrySkipObject();
                case 0x05:
                case 0x06:
                    return true;
                default:
                    return false;
            }
        }

        private bool TrySkipObject()
        {
            while (_offset + 3 <= _data.Length)
            {
                if (_data[_offset] == 0x00 &&
                    _data[_offset + 1] == 0x00 &&
                    _data[_offset + 2] == 0x09)
                {
                    _offset += 3;
                    return true;
                }

                if (!TryReadUtf8(out _))
                    return false;

                if (!TrySkipValue())
                    return false;
            }

            return false;
        }

        private bool TryReadUtf8(out string value)
        {
            value = string.Empty;
            if (_offset + 2 > _data.Length)
                return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_offset, 2));
            _offset += 2;
            if (_offset + length > _data.Length)
                return false;

            value = Encoding.UTF8.GetString(_data.Slice(_offset, length));
            _offset += length;
            return true;
        }

        private bool TryReadByte(out byte value)
        {
            value = 0;
            if (_offset >= _data.Length)
                return false;

            value = _data[_offset++];
            return true;
        }
    }
}
