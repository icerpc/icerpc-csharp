// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
    /// server transport.</summary>
    public class SlicServerTransport : IServerTransport<IMultiplexedNetworkConnection>
    {
        private readonly IServerTransport<ISimpleNetworkConnection> _simpleServerTransport;

        private readonly Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        /// <summary>Constructs a Slic server transport.</summary>
        public SlicServerTransport(IServerTransport<ISimpleNetworkConnection> simpleServerTransport)
            : this(simpleServerTransport, new SlicOptions())
        {
        }

        /// <summary>Constructs a Slic server transport.</summary>
        public SlicServerTransport(
            IServerTransport<ISimpleNetworkConnection> simpleServerTransport,
            SlicOptions slicOptions)
        {
            _simpleServerTransport = simpleServerTransport;
            _slicFrameReaderWriterFactory =
                simpleStream => (new StreamSlicFrameReader(simpleStream), new StreamSlicFrameWriter(simpleStream));
            _slicOptions = slicOptions;
        }

        IListener<IMultiplexedNetworkConnection> IServerTransport<IMultiplexedNetworkConnection>.Listen(
            Endpoint endpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the Slic server transport, where we install log decorators when logging
            // is enabled.

            IListener<ISimpleNetworkConnection> simpleListener = _simpleServerTransport.Listen(endpoint, loggerFactory);

            Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory =
                _slicFrameReaderWriterFactory;

            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger && logger.IsEnabled(LogLevel.Error))
            {
                // TODO: reusing the main LogListenerDecorator results in redundant log messages. Slic should provide
                // its own log decorator to avoid this issue.
                simpleListener = new LogListenerDecorator<ISimpleNetworkConnection>(
                    simpleListener,
                    logger,
                    LogSimpleNetworkConnectionDecorator.Decorate);

                slicFrameReaderWriterFactory = simpleStream =>
                {
                    (ISlicFrameReader reader, ISlicFrameWriter writer) = slicFrameReaderWriterFactory(simpleStream);

                    return (new LogSlicFrameReaderDecorator(reader, logger),
                            new LogSlicFrameWriterDecorator(writer, logger));
                };
            }

            return new SlicListener(simpleListener, slicFrameReaderWriterFactory, _slicOptions);
        }
    }
}
