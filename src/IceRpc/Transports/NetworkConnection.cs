// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>The NetworkConnection static class provides methods to create network connections.</summary>
    // TODO: Remove this class and the CreateSlicConnectionAsync method. The creation of the slic connection
    // needs to be improved. The Slic frame reader and writer should be created through a factory that is
    // setup on the composition root (or an equivalent top-level creation method).
    public static class NetworkConnection
    {
        /// <summary>Creates a Slic multi-stream connection to provide multi-stream connection support for
        /// transports that only provide a single-stream connection implementation.</summary>
        // TODO: This is not public right now because it returns a non-public SlicConnection object (required
        // because IMultiplexedNetworkStreamFactory is not disposable). However, it should be public to allow 3rd-party
        // transports to use use Slic. So ... perhaps return (IMultiplexedNetworkStreamFactory, IDisposable) or make
        // IMultiplexedNetworkStreamFactory inherit from IDisposable.
        internal static async ValueTask<SlicMultiplexedNetworkStreamFactory> CreateSlicConnectionAsync(
            INetworkStream networkStream,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions slicOptions,
            CancellationToken cancel)
        {
            ISlicFrameReader? reader = null;
            ISlicFrameWriter? writer = null;
            SlicMultiplexedNetworkStreamFactory? slicConnection = null;
            try
            {
                reader = new StreamSlicFrameReader(networkStream);
                writer = new StreamSlicFrameWriter(networkStream);
                // TODO: Fix the slic connection creation to not compose the reader/writer here and add back logging.
                // if (logger.IsEnabled(LogLevel.Debug))
                // {
                //     reader = new LogSlicFrameReaderDecorator(reader, logger);
                //     writer = new LogSlicFrameWriterDecorator(writer, logger);
                // }
                slicConnection = new SlicMultiplexedNetworkStreamFactory(
                    reader,
                    writer,
                    isServer,
                    idleTimeout,
                    slicOptions);
                reader = null;
                writer = null;
                await slicConnection.InitializeAsync(cancel).ConfigureAwait(false);
                SlicMultiplexedNetworkStreamFactory returnedSlicConnection = slicConnection;
                slicConnection = null;
                return returnedSlicConnection;
            }
            finally
            {
                writer?.Dispose();
                reader?.Dispose();
                slicConnection?.Dispose();
            }
        }
    }
}
