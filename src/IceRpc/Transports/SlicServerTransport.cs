// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{T}"/>.</summary>
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
            IListener<ISimpleNetworkConnection> simpleListener = _simpleServerTransport.Listen(endpoint, loggerFactory);

            Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory =
                _slicFrameReaderWriterFactory;

            if (loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger && logger.IsEnabled(LogLevel.Error))
            {
                // We add log decorators to all Slic *internal* interfaces.

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
