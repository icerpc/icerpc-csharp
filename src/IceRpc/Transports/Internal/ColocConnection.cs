// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal.Slic;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The network connection class the colocated transport.</summary>
    internal class ColocConnection : INetworkConnection, ISingleStreamConnection
    {
        public int DatagramMaxReceiveSize => throw new InvalidOperationException();

        public TimeSpan IdleTimeout => TimeSpan.MaxValue;

        public bool IsDatagram => false;

        public bool IsSecure => true;

        public bool IsServer { get; }

        public TimeSpan LastActivity => TimeSpan.Zero;

        public Endpoint? LocalEndpoint { get; }

        public ILogger Logger { get; }

        public Endpoint? RemoteEndpoint { get; }

        private readonly SlicOptions _slicOptions;
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private ReadOnlyMemory<byte> _receivedBuffer;
        private SlicConnection? _slicConnection;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        public ValueTask ConnectAsync(CancellationToken cancel) => default;

        public void Dispose()
        {
            _slicConnection?.Dispose();
            _writer.TryComplete(); // Dispose might be called multiple times
        }

        public async ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken cancel)
        {
            // Multi-stream support for a colocated connection is provided by Slic.
            _slicConnection ??= await NetworkConnection.CreateSlicConnection(
                this,
                IsServer,
                TimeSpan.MaxValue,
                _slicOptions,
                Logger,
                cancel).ConfigureAwait(false);
            return _slicConnection;
        }

        public ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken cancel)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                return new(new LogSingleStreamConnectionDecorator(this, Logger));
            }
            else
            {
                return new(this);
            }
        }

    public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }
            return !IsServer;
        }

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedBuffer.Length == 0)
            {
                try
                {
                    _receivedBuffer = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ChannelClosedException exception)
                {
                    throw new ConnectionLostException(exception);
                }
                catch (Exception exception)
                {
                    throw new TransportException(exception);
                }
            }

            if (_receivedBuffer.Length > buffer.Length)
            {
                _receivedBuffer[0..buffer.Length].CopyTo(buffer);
                _receivedBuffer = _receivedBuffer[buffer.Length..];
                return buffer.Length;
            }
            else
            {
                int received = _receivedBuffer.Length;
                _receivedBuffer.CopyTo(buffer[0..received]);
                _receivedBuffer = ReadOnlyMemory<byte>.Empty;
                return received;
            }
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                await _writer.WriteAsync(buffer, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ChannelClosedException exception)
            {
                throw new ConnectionLostException(exception);
            }
            catch (Exception exception)
            {
                throw new TransportException(exception);
            }
        }

        public ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            SendAsync(buffers.ToSingleBuffer(), cancel);

        internal ColocConnection(
            Endpoint endpoint,
            bool isServer,
            SlicOptions slicOptions,
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ILogger logger)
        {
            IsServer = isServer;
            LocalEndpoint = endpoint;
            RemoteEndpoint = endpoint;
            _slicOptions = slicOptions;
            _reader = reader;
            _writer = writer;
            Logger = logger;
        }
    }
}
