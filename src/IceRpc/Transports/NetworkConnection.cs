// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal.Slic;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>The NetworkConnection static class provides methods to create network connections.</summary>
    public static class NetworkConnection
    {
        /// <summary>Creates a Slic multi-stream connection to provide multi-stream connection support for
        /// transports that only provide a single-stream connection implementation.</summary>
        // TODO: This is not public right now because it returns a non-public SlicConnection object (required
        // because IMultiStreamConnection is not disposable). However, it should be public to allow 3rd-party
        // transports to use use Slic. So ... perhaps return (IMultiStreamConnection, IDisposable) or make
        // IMultiStreamConnection inherit from IDisposable.
        internal static async ValueTask<SlicConnection> CreateSlicConnectionAsync(
            ISingleStreamConnection singleStreamConnection,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions slicOptions,
            ILogger logger,
            CancellationToken cancel)
        {
            ISlicFrameReader? reader = null;
            ISlicFrameWriter? writer = null;
            SlicConnection? slicConnection = null;
            try
            {
                reader = new StreamSlicFrameReader(singleStreamConnection);
                writer = new StreamSlicFrameWriter(singleStreamConnection);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    reader = new LogSlicFrameReaderDecorator(reader, logger);
                    writer = new LogSlicFrameWriterDecorator(writer, logger);
                }
                slicConnection = new SlicConnection(
                    reader,
                    writer,
                    isServer,
                    idleTimeout,
                    slicOptions);
                reader = null;
                writer = null;
                await slicConnection.InitializeAsync(cancel).ConfigureAwait(false);
                SlicConnection returnedSlicConnection = slicConnection;
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
