// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
    /// client transport.</summary>
    public class SlicClientTransport : IClientTransport<IMultiplexedNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => _simpleClientTransport.Name;

        private static readonly Func<ISlicFrameReader, ISlicFrameReader> _defaultSlicFrameReaderDecorator =
            reader => reader;

        private static readonly Func<ISlicFrameWriter, ISlicFrameWriter> _defaultSlicFrameWriterDecorator =
            writer => writer;

        private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;
        private readonly SlicTransportOptions _slicTransportOptions;

        /// <summary>Constructs a Slic client transport.</summary>
        /// <param name="options">The options to configure the Slic transport.</param>
        /// <param name="simpleClientTransport">The simple client transport.</param>
        public SlicClientTransport(
            SlicTransportOptions options,
            IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
        {
            _simpleClientTransport = simpleClientTransport;
            _slicTransportOptions = options;
        }

        /// <summary>Constructs a Slic client transport.</summary>
        /// <param name="simpleClientTransport">The simple client transport.</param>
        public SlicClientTransport(IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
            : this(new(), simpleClientTransport)
        {
        }

        /// <inheritdoc/>
        public bool CheckParams(Endpoint endpoint) => _simpleClientTransport.CheckParams(endpoint);

        /// <inheritdoc/>
        public IMultiplexedNetworkConnection CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            // This is the composition root of the Slic client transport, where we install log decorators when logging
            // is enabled.

            ISimpleNetworkConnection simpleNetworkConnection =
                _simpleClientTransport.CreateConnection(remoteEndpoint, authenticationOptions, logger);

            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator = _defaultSlicFrameReaderDecorator;
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator = _defaultSlicFrameWriterDecorator;

            if (logger.IsEnabled(LogLevel.Error))
            {
                simpleNetworkConnection = new LogSimpleNetworkConnectionDecorator(
                    simpleNetworkConnection,
                    remoteEndpoint,
                    false,
                    logger);
                slicFrameReaderDecorator = reader => new LogSlicFrameReaderDecorator(reader, logger);
                slicFrameWriterDecorator = writer => new LogSlicFrameWriterDecorator(writer, logger);
            }

            return new SlicNetworkConnection(
                simpleNetworkConnection,
                isServer: false,
                remoteEndpoint.Protocol,
                slicFrameReaderDecorator,
                slicFrameWriterDecorator,
                _slicTransportOptions);
        }
    }
}
