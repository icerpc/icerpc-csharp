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

        private readonly Func<ISlicFrameReader, ISlicFrameReader> _slicFrameReaderDecorator;
        private readonly Func<ISlicFrameWriter, ISlicFrameWriter> _slicFrameWriterDecorator;
        private readonly SlicOptions _slicOptions;

        /// <inheritdoc/>
        Endpoint IServerTransport<IMultiplexedNetworkConnection>.DefaultEndpoint =>
            _simpleServerTransport.DefaultEndpoint;

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
            _slicFrameReaderDecorator = reader => reader;
            _slicFrameWriterDecorator = writer => writer;
            _slicOptions = slicOptions;
        }

        IListener<IMultiplexedNetworkConnection> IServerTransport<IMultiplexedNetworkConnection>.Listen(
            Endpoint endpoint,
            ILogger logger)
        {
            // This is the composition root of the Slic server transport, where we install log decorators when logging
            // is enabled.

            IListener<ISimpleNetworkConnection> simpleListener = _simpleServerTransport.Listen(endpoint, logger);

            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator = _slicFrameReaderDecorator;
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator = _slicFrameWriterDecorator;

            if (logger.IsEnabled(LogLevel.Error))
            {
                // TODO: should we add a log decorator over simple listener too?

                slicFrameReaderDecorator = reader => new LogSlicFrameReaderDecorator(reader, logger);
                slicFrameWriterDecorator = writer => new LogSlicFrameWriterDecorator(writer, logger);
            }

            return new SlicListener(simpleListener, slicFrameReaderDecorator, slicFrameWriterDecorator, _slicOptions);
        }
    }
}
