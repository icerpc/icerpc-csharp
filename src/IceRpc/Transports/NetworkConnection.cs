// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using IceRpc.Transports.Internal.Slic;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>The NetworkConnection factory methods for creating network connections.</summary>
    public static class NetworkConnection
    {
        /// <summary>Creates a new network socket connection based on <see cref="NetworkSocket"/></summary>
        /// <param name="socket">The network socket. It can be a client socket or server socket, and
        /// the resulting connection will be likewise a client or server network connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the
        /// endpoint the server is listening on.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        /// <param name="idleTimeout">The connection idle timeout.</param>
        /// <param name="slicOptions">The Slic options.</param>
        /// <param name="logger">The logger.</param>
        public static INetworkConnection CreateNetworkSocketConnection(
            NetworkSocket socket,
            Endpoint endpoint,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions slicOptions,
            ILogger logger) =>
            CreateLogNetworkConnection(new NetworkSocketConnection(
                socket,
                endpoint,
                isServer: isServer,
                idleTimeout: idleTimeout,
                slicOptions,
                logger),
                logger);

        /// <summary>Creates a network connection decorator to log received and sent bytes from the network
        /// connection.</summary>
        public static INetworkConnection CreateLogNetworkConnection(
            INetworkConnection networkConnection,
            ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                return new LogNetworkConnectionDecorator(networkConnection, logger);
            }
            else
            {
                return networkConnection;
            }
        }

        internal static async ValueTask<SlicConnection> CreateSlicConnection(
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
                slicConnection = new SlicConnection(reader, writer, isServer, idleTimeout, slicOptions);
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
