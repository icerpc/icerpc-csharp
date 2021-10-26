// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
    /// client transport.</summary>
    public class SlicClientTransport : IClientTransport<IMultiplexedNetworkConnection>
    {
        private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;
        private readonly Func<ISlicFrameReader, ISlicFrameReader> _slicFrameReaderDecorator;
        private readonly Func<ISlicFrameWriter, ISlicFrameWriter> _slicFrameWriterDecorator;

        private readonly SlicOptions _slicOptions;

        /// <summary>Constructs a Slic client transport.</summary>
        public SlicClientTransport(IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
            : this(simpleClientTransport, new())
        {
        }

        /// <summary>Constructs a Slic client transport.</summary>
        public SlicClientTransport(
            IClientTransport<ISimpleNetworkConnection> simpleClientTransport,
            SlicOptions slicOptions)
        {
            _simpleClientTransport = simpleClientTransport;
            _slicFrameReaderDecorator = reader => reader;
            _slicFrameWriterDecorator = writer => writer;
            _slicOptions = slicOptions;
        }

        IMultiplexedNetworkConnection IClientTransport<IMultiplexedNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the Slic client transport, where we install log decorators when logging
            // is enabled.

            ISimpleNetworkConnection simpleNetworkConnection =
                _simpleClientTransport.CreateConnection(remoteEndpoint, loggerFactory);

            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator = _slicFrameReaderDecorator;
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator = _slicFrameWriterDecorator;

            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger && logger.IsEnabled(LogLevel.Error))
            {
                // TODO: reusing the main LogSimpleNetworkConnectionDecorator results in redundant log messages. Slic
                // should provide its own log decorator to avoid this issue.
                simpleNetworkConnection = new LogSimpleNetworkConnectionDecorator(simpleNetworkConnection,
                                                                                  isServer: false,
                                                                                  remoteEndpoint,
                                                                                  logger);

                slicFrameReaderDecorator = reader => new LogSlicFrameReaderDecorator(reader, logger);
                slicFrameWriterDecorator = writer => new LogSlicFrameWriterDecorator(writer, logger);
            }

            return new SlicNetworkConnection(simpleNetworkConnection,
                                             isServer: false,
                                             slicFrameReaderDecorator,
                                             slicFrameWriterDecorator,
                                             _slicOptions);
        }
    }
}
