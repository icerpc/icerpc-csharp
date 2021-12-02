// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The colocated network connection class to exchange data within the same process. The implementation
    /// copies the send buffer into the receive buffer.</summary>
    internal class ColocNetworkConnection : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => true;

        TimeSpan INetworkConnection.LastActivity => TimeSpan.Zero;

        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private readonly PipeReader _reader;
        private readonly PipeWriter _writer;

        public Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel) =>
            Task.FromResult(new NetworkConnectionInformation(_endpoint, _endpoint, TimeSpan.MaxValue, null));

        public ValueTask DisposeAsync() => _writer.CompleteAsync();

        public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }
            return !_isServer;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            ReadResult readResult = await _reader.ReadAsync(cancel).ConfigureAwait(false);
            if (readResult.IsCompleted)
            {
                await _reader.CompleteAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(ColocNetworkConnection));
            }

            // We could eventually add a CopyTo(this ReadOnlySequence<byte> src, Memory<byte> dest) extension method
            // if we need this in other places.
            int read;
            if (readResult.Buffer.IsSingleSegment)
            {
                read = CopySegmentToMemory(readResult.Buffer.First, buffer);
            }
            else
            {
                read = 0;
                foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                {

                    read += CopySegmentToMemory(segment, buffer[read..]);
                    if (read == buffer.Length)
                    {
                        break;
                    }
                }
            }
            _reader.AdvanceTo(readResult.Buffer.GetPosition(read));
            return read;

            static int CopySegmentToMemory(ReadOnlyMemory<byte> source, Memory<byte> destination)
            {
                if (source.Length > destination.Length)
                {
                    source[0..destination.Length].CopyTo(destination);
                    return destination.Length;
                }
                else
                {
                    source.CopyTo(destination);
                    return source.Length;
                }
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            for (int i = 0; i < buffers.Length; ++i)
            {
                try
                {
                    FlushResult result = await _writer.WriteAsync(buffers.Span[i], cancel).ConfigureAwait(false);
                    if (result.IsCompleted)
                    {
                        throw new ObjectDisposedException(nameof(ColocNetworkConnection));
                    }
                    else if (result.IsCanceled)
                    {
                        throw new OperationCanceledException();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Pipe is completed.
                    throw new ObjectDisposedException(nameof(ColocNetworkConnection));
                }
            }
        }

        internal ColocNetworkConnection(Endpoint endpoint, bool isServer, PipeReader reader, PipeWriter writer)
        {
            _endpoint = endpoint;
            _isServer = isServer;
            _reader = reader;
            _writer = writer;
        }
    }
}
