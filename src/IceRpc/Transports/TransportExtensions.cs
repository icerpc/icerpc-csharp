// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Extensions to enable logging for client and server transports.</summary>
    public static partial class TransportExtensions
    {
        /// <summary>Returns a client transport that enables logging for the <see cref="UdpClientTransport"/></summary>.
        /// <param name="transport">The client transport.</param>
        /// <param name="loggerFactory">The logger factory used to create the <see cref="ILogger"/> to log transport
        /// traces.</param>
        /// <returns>The client transport with logging enabled.</returns>
        public static IClientTransport UseLoggerFactory(
            this UdpClientTransport transport,
            ILoggerFactory loggerFactory)
        {
            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(LogLevel.Error))
            {
                return new LogClientTransportDecorator(transport, logger);
            }
            else
            {
                return transport;
            }
        }

        /// <summary>Returns a client transport that enables logging for the given <see
        /// cref="SimpleClientTransport"/></summary>.
        /// <param name="transport">The client transport.</param>
        /// <param name="loggerFactory">The logger factory used to create the <see cref="ILogger"/> to log transport
        /// traces.</param>
        /// <returns>The client transport with logging enabled.</returns>
        public static IClientTransport UseLoggerFactory(
            this SimpleClientTransport transport,
            ILoggerFactory loggerFactory)
        {
            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(LogLevel.Error))
            {
                return new LogSlicClientTransportDecorator(transport, logger);
            }
            else
            {
                return transport;
            }
        }

        /// <summary>Returns a server transport that enables logging for the given <see
        /// cref="IServerTransport"/></summary>.
        /// <param name="transport">The server transport.</param>
        /// <param name="loggerFactory">The logger factory used to create the <see cref="ILogger"/> to log transport
        /// traces.</param>
        /// <returns>The server transport with logging enabled.</returns>
        public static IServerTransport UseLoggerFactory(
            this UdpServerTransport transport,
            ILoggerFactory loggerFactory)
        {
            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(LogLevel.Error))
            {
                return new LogServerTransportDecorator(transport, logger);
            }
            else
            {
                return transport;
            }
        }

        /// <summary>Returns a server transport that enables logging for the given <see
        /// cref="SimpleServerTransport"/></summary>.
        /// <param name="transport">The server transport.</param>
        /// <param name="loggerFactory">The logger factory used to create the <see cref="ILogger"/> to log transport
        /// traces.</param>
        /// <returns>The server transport with logging enabled.</returns>
        public static IServerTransport UseLoggerFactory(
            this SimpleServerTransport transport,
            ILoggerFactory loggerFactory)
        {
            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(LogLevel.Error))
            {
                return new LogSlicServerTransportDecorator(transport, logger);
            }
            else
            {
                return transport;
            }
        }
    }
}
