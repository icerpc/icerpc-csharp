// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal sealed class WSConnection : SingleStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            new WSConnectionInformation(
                _bufferedConnection.NetworkSocket!,
                ((TcpConnection)_bufferedConnection.Underlying).SslStream)
            {
                Headers = _parser.GetHeaders()
            };

        /// <inheritdoc/>
        internal override System.Net.Sockets.Socket? NetworkSocket => _bufferedConnection.NetworkSocket;

        internal enum OpCode : byte
        {
            Continuation = 0x0,
            Text = 0x1,
            Data = 0x2,
            Close = 0x8,
            Ping = 0x9,
            Pong = 0xA
        }

        private enum ClosureStatusCode : short
        {
            Normal = 1000,
            Shutdown = 1001
        }

        private const byte FlagFinal = 0x80;   // Last frame
        private const byte FlagMasked = 0x80;   // Payload is masked
        private const string IceProtocol = "ice.zeroc.com";
        private const string WsUUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private static readonly UTF8Encoding _utf8 = new(false, true);

        private TaskCompletionSource? _closingTaskCompletionSource;
        private bool _incoming;
        private string _key;
        private readonly HttpParser _parser;
        private readonly object _mutex = new();
        private readonly BufferedReceiveOverSingleStreamConnection _bufferedConnection;
        private readonly RandomNumberGenerator _rand;
        private bool _receiveLastFrame;
        private readonly byte[] _receiveMask = new byte[4];
        private int _receivePayloadLength;
        private int _receivePayloadOffset;
        private readonly byte[] _sendMask;
        private Task _sendTask = Task.CompletedTask;

        public override async ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Endpoint? remoteEndpoint =
                await _bufferedConnection.AcceptAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
            var wsEndpoint = (WSEndpoint)endpoint;
            await InitializeAsync(true, wsEndpoint.Host, wsEndpoint.Resource, cancel).ConfigureAwait(false);
            return remoteEndpoint;
        }

        public override async ValueTask CloseAsync(long errorCode, CancellationToken cancel)
        {
            lock (_mutex)
            {
                if (_closingTaskCompletionSource != null)
                {
                    // We already received the close frame.
                    return;
                }
                _closingTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            // Send the close frame if the close frame.
            byte[] payload = new byte[2];
            short reason = IPAddress.HostToNetworkOrder((short)ClosureStatusCode.Shutdown);
            MemoryMarshal.Write(payload, ref reason);
            await SendImplAsync(OpCode.Close, new ReadOnlyMemory<byte>[] { payload }, cancel).ConfigureAwait(false);

            await _closingTaskCompletionSource.Task.IceWaitAsync(cancel).ConfigureAwait(false);
        }

        public override async ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Endpoint localEndpoint =
                await _bufferedConnection.ConnectAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
            var wsEndpoint = (WSEndpoint)endpoint;
            await InitializeAsync(false, wsEndpoint.Host, wsEndpoint.Resource, cancel).ConfigureAwait(false);
            return localEndpoint;
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            // If we've fully read the previous DATA frame payload, read a new frame
            Debug.Assert(_receivePayloadOffset <= _receivePayloadLength);
            if (_receivePayloadOffset == _receivePayloadLength)
            {
                _receivePayloadLength = await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                _receivePayloadOffset = 0;
            }

            if (_receivePayloadLength == 0)
            {
                throw new ConnectionLostException();
            }

            // Read the payload
            int length = Math.Min(_receivePayloadLength - _receivePayloadOffset, buffer.Length);
            int received = await _bufferedConnection.ReceiveAsync(buffer[0..length], cancel).ConfigureAwait(false);

            if (_incoming)
            {
                Unmask(buffer, _receivePayloadOffset, received);
            }
            _receivePayloadOffset += received;
            return received;
        }

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            SendAsync(new ReadOnlyMemory<byte>[] { buffer }, cancel);

        public override ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel) => SendImplAsync(OpCode.Data, buffers, cancel);

        protected override void Dispose(bool disposing)
        {
            _closingTaskCompletionSource?.TrySetResult();
            _bufferedConnection.Dispose();
            _rand.Dispose();
        }

        internal WSConnection(TcpConnection tcpConnection)
            : base(tcpConnection.Logger)
        {
            _bufferedConnection = new BufferedReceiveOverSingleStreamConnection(tcpConnection);
            _parser = new HttpParser();
            _receiveLastFrame = true;
            _sendMask = new byte[4];
            _key = "";
            _rand = RandomNumberGenerator.Create();
        }

        private async ValueTask InitializeAsync(bool incoming, string host, string resource, CancellationToken cancel)
        {
            _incoming = incoming;

            try
            {
                // The server waits for the client's upgrade request, the client sends the upgrade request.
                if (!incoming)
                {
                    // Compose the upgrade request.
                    var sb = new StringBuilder();
                    sb.Append("GET " + resource + " HTTP/1.1\r\n");
                    sb.Append("Host: " + host + "\r\n");
                    sb.Append("Upgrade: websocket\r\n");
                    sb.Append("Connection: Upgrade\r\n");
                    sb.Append("Sec-WebSocket-Protocol: " + IceProtocol + "\r\n");
                    sb.Append("Sec-WebSocket-Version: 13\r\n");
                    sb.Append("Sec-WebSocket-Key: ");

                    // The value for Sec-WebSocket-Key is a 16-byte random number, encoded with Base64.
                    byte[] key = new byte[16];
                    _rand.GetBytes(key);
                    _key = Convert.ToBase64String(key);
                    sb.Append(_key + "\r\n\r\n"); // EOM
                    byte[] data = _utf8.GetBytes(sb.ToString());
                    await _bufferedConnection.SendAsync(data, cancel).ConfigureAwait(false);
                }

                // Try to read the client's upgrade request or the server's response.
                Memory<byte> httpBuffer = Memory<byte>.Empty;
                while (true)
                {
                    ReadOnlyMemory<byte> buffer = await _bufferedConnection.ReceiveAsync(0, cancel).ConfigureAwait(false);
                    if (httpBuffer.Length + buffer.Length > 16 * 1024)
                    {
                        throw new InvalidDataException("WebSocket HTTP upgrade request too large");
                    }

                    Memory<byte> tmpBuffer = new byte[httpBuffer.Length + buffer.Length];
                    if (httpBuffer.Length > 0)
                    {
                        httpBuffer.CopyTo(tmpBuffer);
                    }
                    buffer.CopyTo(tmpBuffer[httpBuffer.Length..]);
                    httpBuffer = tmpBuffer;

                    // Check if we have enough data for a complete frame.
                    int endPos = HttpParser.IsCompleteMessage(httpBuffer.Span);
                    if (endPos != -1)
                    {
                        // Add back the un-consumed data to the buffer.
                        _bufferedConnection.Rewind(httpBuffer.Length - endPos);
                        httpBuffer = httpBuffer.Slice(0, endPos);
                        break; // Done
                    }
                }

                if (_parser.Parse(httpBuffer.Span))
                {
                    if (_incoming)
                    {
                        (bool addProtocol, string key) = ReadUpgradeRequest();

                        // Compose the response.
                        var sb = new StringBuilder();
                        sb.Append("HTTP/1.1 101 Switching Protocols\r\n");
                        sb.Append("Upgrade: websocket\r\n");
                        sb.Append("Connection: Upgrade\r\n");
                        if (addProtocol)
                        {
                            sb.Append($"Sec-WebSocket-Protocol: {IceProtocol}\r\n");
                        }

                        // The response includes:
                        //
                        // "A |Sec-WebSocket-Accept| header field.  The value of this header field is constructed
                        // by concatenating /key/, defined above in step 4 in Section 4.2.2, with the string
                        // "258EAFA5-E914-47DA-95CA-C5AB0DC85B11", taking the SHA-1 hash of this concatenated value
                        // to obtain a 20-byte value and base64-encoding (see Section 4 of [RFC4648]) this 20-byte
                        // hash.
                        sb.Append("Sec-WebSocket-Accept: ");
                        string input = key + WsUUID;
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                        using var sha1 = SHA1.Create();
                        byte[] hash = sha1.ComputeHash(_utf8.GetBytes(input));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
                        sb.Append(Convert.ToBase64String(hash) + "\r\n" + "\r\n"); // EOM

                        byte[] data = _utf8.GetBytes(sb.ToString());
                        await _bufferedConnection.SendAsync(data, cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        ReadUpgradeResponse();
                    }
                }
                else
                {
                    throw new InvalidDataException("incomplete WebSocket request frame");
                }
            }
            catch (Exception ex)
            {
                Logger.LogHttpUpgradeRequestFailed(ex);
                throw;
            }

            if (_incoming)
            {
                Logger.LogHttpUpgradeRequestAccepted();
            }
            else
            {
                Logger.LogHttpUpgradeRequestSucceed();
            }
        }

        private int PrepareHeaderForSend(OpCode opCode, int payloadSize, Memory<byte> buffer)
        {
            int i = 0;

            // Set the opcode - this is the one and only data frame.
            buffer.Span[i++] = (byte)((byte)opCode | FlagFinal);

            // Set the payload length.
            if (payloadSize <= 125)
            {
                buffer.Span[i++] = (byte)payloadSize;
            }
            else if (payloadSize > 125 && payloadSize <= 65535)
            {
                // Use an extra 16 bits to encode the payload length.
                buffer.Span[i++] = 126;
                short length = IPAddress.HostToNetworkOrder((short)payloadSize);
                MemoryMarshal.Write(buffer.Span.Slice(i, 2), ref length);
                i += 2;
            }
            else if (payloadSize > 65535)
            {
                // Use an extra 64 bits to encode the payload length.
                buffer.Span[i++] = 127;
                long length = IPAddress.HostToNetworkOrder((long)payloadSize);
                MemoryMarshal.Write(buffer.Span.Slice(i, 8), ref length);
                i += 8;
            }

            if (!_incoming)
            {
                // Add a random 32-bit mask to every outgoing frame, copy the payload data, and apply the mask.
                buffer.Span[1] = (byte)(buffer.Span[1] | FlagMasked);
                _rand.GetBytes(_sendMask);
                _sendMask.AsMemory().CopyTo(buffer[i..]);
                i += _sendMask.Length;
            }
            return i;
        }

        private async ValueTask<int> ReceiveFrameAsync(CancellationToken cancel)
        {
            while (true)
            {
                // Read the first 2 bytes of the WS frame header
                ReadOnlyMemory<byte> header = await _bufferedConnection.ReceiveAsync(2, cancel).ConfigureAwait(false);
                // Most-significant bit indicates if this is the last frame, least-significant four bits hold the opcode.
                var opCode = (OpCode)(header.Span[0] & 0xf);

                // Check if the OpCode is compatible of the FIN flag of the previous frame.
                if (opCode == OpCode.Data && !_receiveLastFrame)
                {
                    throw new InvalidDataException("invalid WebSocket data frame, no FIN on previous frame");
                }
                else if (opCode == OpCode.Continuation && _receiveLastFrame)
                {
                    throw new InvalidDataException("invalid WebSocket continuation frame, previous frame FIN set");
                }

                // Remember the FIN flag of this frame for the previous check.
                _receiveLastFrame = (header.Span[0] & FlagFinal) == FlagFinal;

                // Messages sent by a client must be masked; frames sent by a server must not be masked.
                bool masked = (header.Span[1] & FlagMasked) == FlagMasked;
                if (masked != _incoming)
                {
                    throw new InvalidDataException("invalid WebSocket masking");
                }

                // Extract the payload length, which can have the following values:
                // 0-125: The payload length
                // 126:   The subsequent two bytes contain the payload length
                // 127:   The subsequent eight bytes contain the payload length
                int payloadLength = header.Span[1] & 0x7f;
                if (payloadLength == 126)
                {
                    header = await _bufferedConnection.ReceiveAsync(2, cancel).ConfigureAwait(false);
                    ushort length = header.Span.ReadUShort();
                    payloadLength = (ushort)System.Net.IPAddress.NetworkToHostOrder((short)length);
                }
                else if (payloadLength == 127)
                {
                    header = await _bufferedConnection.ReceiveAsync(8, cancel).ConfigureAwait(false);
                    long length = System.Net.IPAddress.NetworkToHostOrder(header.Span.ReadLong());
                    if (length > int.MaxValue)
                    {
                        // We never send payloads with such length, we shouldn't get any.
                        throw new InvalidDataException("WebSocket payload length is not supported");
                    }
                    payloadLength = (int)length;
                }

                if (_incoming)
                {
                    // Read the mask if this is an incoming connection.
                    (await _bufferedConnection.ReceiveAsync(4, cancel).ConfigureAwait(false)).CopyTo(_receiveMask);
                }

                Logger.LogReceivedWebSocketFrame(opCode, payloadLength);

                switch (opCode)
                {
                    case OpCode.Text:
                    {
                        throw new InvalidDataException("WebSocket text frames not supported");
                    }
                    case OpCode.Data:
                    case OpCode.Continuation:
                    {
                        if (payloadLength <= 0)
                        {
                            throw new InvalidDataException("WebSocket payload length is invalid");
                        }
                        return payloadLength;
                    }
                    case OpCode.Close:
                    {
                        // Read the Close frame payload.
                        ReadOnlyMemory<byte> payloadBuffer =
                            await _bufferedConnection.ReceiveAsync(payloadLength, cancel).ConfigureAwait(false);

                        byte[] payload = payloadBuffer.ToArray();
                        if (_incoming)
                        {
                            Unmask(payload, 0, payload.Length);
                        }

                        lock (_mutex)
                        {
                            if (_closingTaskCompletionSource != null)
                            {
                                _closingTaskCompletionSource.TrySetResult();
                                return 0;
                            }

                            _closingTaskCompletionSource = new();
                            _closingTaskCompletionSource.SetResult();
                        }

                        // Send back a close frame.
                        await SendImplAsync(OpCode.Close,
                                            new ReadOnlyMemory<byte>[] { payload }, cancel).ConfigureAwait(false);
                        break;
                    }
                    case OpCode.Ping:
                    {
                        // Read the ping payload.
                        ReadOnlyMemory<byte> payload =
                            await _bufferedConnection.ReceiveAsync(payloadLength, cancel).ConfigureAwait(false);

                        // Send a Pong frame with the received payload.
                        await SendImplAsync(OpCode.Pong,
                                            new ReadOnlyMemory<byte>[] { payload }, cancel).ConfigureAwait(false);
                        break;
                    }
                    case OpCode.Pong:
                    {
                        // Read the pong payload.
                        await _bufferedConnection.ReceiveAsync(payloadLength, cancel).ConfigureAwait(false);

                        // Nothing to do, this can be received even if we don't send a ping frame if the peer sends
                        // an unidirectional heartbeat.
                        break;
                    }
                    default:
                    {
                        throw new InvalidDataException($"unsupported WebSocket opcode: {opCode}");
                    }
                }
            }
        }

        private (bool, string) ReadUpgradeRequest()
        {
            // HTTP/1.1
            if (_parser.VersionMajor() != 1 || _parser.VersionMinor() != 1)
            {
                throw new InvalidDataException("unsupported HTTP version");
            }

            // "An |Upgrade| header field containing the value 'websocket', treated as an ASCII case-insensitive value."
            string? value = _parser.GetHeader("Upgrade", true);
            if (value == null)
            {
                throw new InvalidDataException("missing value for Upgrade field");
            }
            else if (value != "websocket")
            {
                throw new InvalidDataException($"invalid value '{value}' for Upgrade field");
            }

            // "A |Connection| header field that includes the token 'Upgrade', treated as an ASCII case-insensitive
            // value.
            value = _parser.GetHeader("Connection", true);
            if (value == null)
            {
                throw new InvalidDataException("missing value for Connection field");
            }
            else if (!value.Contains("upgrade"))
            {
                throw new InvalidDataException($"invalid value '{value}' for Connection field");
            }

            // "A |Sec-WebSocket-Version| header field, with a value of 13."
            value = _parser.GetHeader("Sec-WebSocket-Version", false);
            if (value == null)
            {
                throw new InvalidDataException("missing value for WebSocket version");
            }
            else if (value != "13")
            {
                throw new InvalidDataException($"unsupported WebSocket version '{value}'");
            }

            // "Optionally, a |Sec-WebSocket-Protocol| header field, with a list of values indicating which protocols
            // the client would like to speak, ordered by preference."
            bool addProtocol = false;
            value = _parser.GetHeader("Sec-WebSocket-Protocol", true);
            if (value != null)
            {
                string[]? protocols = StringUtil.SplitString(value, ",");
                if (protocols == null)
                {
                    throw new InvalidDataException($"invalid value '{value}' for WebSocket protocol");
                }

                foreach (string protocol in protocols)
                {
                    if (protocol.Trim() != IceProtocol)
                    {
                        throw new InvalidDataException($"unknown value '{protocol}' for WebSocket protocol");
                    }
                    addProtocol = true;
                }
            }

            // "A |Sec-WebSocket-Key| header field with a base64-encoded value that, when decoded, is 16 bytes in
            // length."
            string? key = _parser.GetHeader("Sec-WebSocket-Key", false);
            if (key == null)
            {
                throw new InvalidDataException("missing value for WebSocket key");
            }

            byte[] decodedKey = Convert.FromBase64String(key);
            if (decodedKey.Length != 16)
            {
                throw new InvalidDataException($"invalid value '{key}' for WebSocket key");
            }

            return (addProtocol, key);
        }

        private void ReadUpgradeResponse()
        {
            // HTTP/1.1
            if (_parser.VersionMajor() != 1 || _parser.VersionMinor() != 1)
            {
                throw new InvalidDataException("unsupported HTTP version");
            }

            // "If the status code received from the server is not 101, the client handles the response per HTTP
            // [RFC2616] procedures. In particular, the client might perform authentication if it receives a 401 status
            // code; the server might redirect the client using a 3xx status code (but clients are not required to
            // follow them), etc."
            if (_parser.Status() != 101)
            {
                var sb = new StringBuilder("unexpected status value " + _parser.Status());
                if (_parser.Reason().Length > 0)
                {
                    sb.Append(":\n" + _parser.Reason());
                }
                throw new InvalidDataException(sb.ToString());
            }

            // "If the response lacks an |Upgrade| header field or the |Upgrade| header field contains a value that is
            // not an ASCII case-insensitive match for the value "websocket", the client MUST_Fail the WebSocket
            // Connection_."
            string? value = _parser.GetHeader("Upgrade", true);
            if (value == null)
            {
                throw new InvalidDataException("missing value for Upgrade field");
            }
            else if (value != "websocket")
            {
                throw new InvalidDataException($"invalid value '{value}' for Upgrade field");
            }

            // "If the response lacks a |Connection| header field or the |Connection| header field doesn't contain a
            // token that is an ASCII case-insensitive match for the value "Upgrade", the client MUST _Fail the
            // WebSocket Connection_."
            value = _parser.GetHeader("Connection", true);
            if (value == null)
            {
                throw new InvalidDataException("missing value for Connection field");
            }
            else if (!value.Contains("upgrade"))
            {
                throw new InvalidDataException($"invalid value '{value}' for Connection field");
            }

            // "If the response includes a |Sec-WebSocket-Protocol| header field and this header field indicates the
            // use of a subprotocol that was not present in the client's handshake (the server has indicated a
            // subprotocol not requested by the client), the client MUST _Fail the WebSocket Connection_."
            value = _parser.GetHeader("Sec-WebSocket-Protocol", true);
            if (value != null && value != IceProtocol)
            {
                throw new InvalidDataException($"invalid value '{value}' for WebSocket protocol");
            }

            // "If the response lacks a |Sec-WebSocket-Accept| header field or the |Sec-WebSocket-Accept| contains a
            // value other than the base64-encoded SHA-1 of the concatenation of the |Sec-WebSocket-Key| (as a string,
            // not base64-decoded) with the string "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" but ignoring any leading and
            // trailing whitespace, the client MUST _Fail the WebSocket Connection_."
            value = _parser.GetHeader("Sec-WebSocket-Accept", false);
            if (value == null)
            {
                throw new InvalidDataException("missing value for Sec-WebSocket-Accept");
            }

            string input = _key + WsUUID;
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            using var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(_utf8.GetBytes(input));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
            if (value != Convert.ToBase64String(hash))
            {
                throw new InvalidDataException($"invalid value '{value}' for Sec-WebSocket-Accept");
            }
        }

        private async ValueTask SendImplAsync(
            OpCode opCode,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            // Write can be called concurrently because it's called from both ReadAsync and WriteAsync. For example,
            // the reading of a ping frame requires writing a pong frame.
            Task task;
            lock (_mutex)
            {
                ValueTask writeTask = PerformWriteAsync(opCode, buffers, cancel);

                // Optimization: we check if the write completed already and avoid creating a Task if it did.
                if (writeTask.IsCompletedSuccessfully)
                {
                    _sendTask = Task.CompletedTask;
                    return;
                }

                task = writeTask.AsTask();
                _sendTask = task;
            }
            await task.ConfigureAwait(false);

            async ValueTask PerformWriteAsync(
                OpCode opCode,
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
                CancellationToken cancel)
            {
                // Wait for the current write to be done.
                await _sendTask.ConfigureAwait(false);

                int size = buffers.GetByteCount();
                ReadOnlyMemory<byte> buffer = buffers.Span[0];

                IMemoryOwner<byte>? sendBufferOwner = MemoryPool<byte>.Shared.Rent(minBufferSize: 16 + buffer.Length);
                try
                {
                    Memory<byte> sendBuffer = sendBufferOwner.Memory;

                    int headerSize = PrepareHeaderForSend(opCode, size, sendBuffer);

                    buffer.CopyTo(sendBuffer[headerSize..]);
                    if (!_incoming && opCode != OpCode.Pong)
                    {
                        Mask(sendBuffer.Slice(headerSize, buffer.Length), 0);
                    }
                    int index = buffer.Length;

                    Logger.LogSendingWebSocketFrame(opCode, size);

                    await _bufferedConnection.SendAsync(sendBuffer[0..(headerSize + buffer.Length)],
                                                        cancel).ConfigureAwait(false);

                    // Send remaining buffers, if any.
                    buffers = buffers[1..];

                    if (buffers.Length > 0)
                    {
                        // For an outgoing connection, each frame must be masked with a random 32-bit value.
                        if (!_incoming && opCode != OpCode.Pong)
                        {
                            int buffersIndex = 0;
                            while (buffersIndex < buffers.Length)
                            {
                                buffer = buffers.Span[buffersIndex++];
                                if (buffer.Length > 0)
                                {
                                    if (buffer.Length > sendBuffer.Length)
                                    {
                                        sendBufferOwner.Dispose();
                                        sendBufferOwner = null;
                                        sendBufferOwner = MemoryPool<byte>.Shared.Rent(buffer.Length);
                                        sendBuffer = sendBufferOwner.Memory;
                                    }
                                    buffer.CopyTo(sendBuffer);
                                    Mask(sendBuffer[0..buffer.Length], index);
                                    index += buffer.Length;
                                    await _bufferedConnection.SendAsync(sendBuffer[0..buffer.Length],
                                                                        cancel).ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            await _bufferedConnection.SendAsync(buffers, cancel).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    sendBufferOwner?.Dispose();
                }

                void Mask(Memory<byte> payload, int start)
                {
                    Span<byte> bufferAsSpan = payload.Span;
                    for (int i = 0; i < payload.Length; ++i)
                    {
                        bufferAsSpan[i] = (byte)(bufferAsSpan[i] ^ _sendMask[(start + i) % 4]);
                    }
                }
            }
        }

        private void Unmask(Memory<byte> buffer, int offset, int length)
        {
            Span<byte> bufferAsSpan = buffer.Span;
            for (int i = 0; i < length; ++i)
            {
                bufferAsSpan[i] = (byte)(bufferAsSpan[i] ^ _receiveMask[offset++ % 4]);
            }
        }
    }
}
