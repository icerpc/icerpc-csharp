// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
    /// server transport.</summary>
    public class SlicServerTransport : IServerTransport<IMultiplexedNetworkConnection>
    {
        private static readonly Func<ISlicFrameReader, ISlicFrameReader> _defaultSlicFrameReaderDecorator =
            reader => reader;
        private static readonly Func<ISlicFrameWriter, ISlicFrameWriter> _defaultSlicFrameWriterDecorator =
            writer => writer;
        private readonly IServerTransport<ISimpleNetworkConnection> _simpleServerTransport;
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
            _slicOptions = slicOptions;
            _slicOptions.Check();
        }

        IListener<IMultiplexedNetworkConnection> IServerTransport<IMultiplexedNetworkConnection>.Listen(
            Endpoint endpoint,
            ILogger logger)
        {
            // This is the composition root of the Slic server transport, where we install log decorators when logging
            // is enabled.

            IListener<ISimpleNetworkConnection> simpleListener = _simpleServerTransport.Listen(endpoint, logger);

            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator = _defaultSlicFrameReaderDecorator;
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator = _defaultSlicFrameWriterDecorator;

            if (logger.IsEnabled(LogLevel.Error))
            {
                simpleListener = new LogListenerDecorator<ISimpleNetworkConnection>(
                    simpleListener,
                    logger,
                    LogSimpleNetworkConnectionDecorator.Decorate);
                slicFrameReaderDecorator = reader => new LogSlicFrameReaderDecorator(reader, logger);
                slicFrameWriterDecorator = writer => new LogSlicFrameWriterDecorator(writer, logger);
            }

            return new SlicListener(simpleListener, slicFrameReaderDecorator, slicFrameWriterDecorator, _slicOptions);
        }
    }
}
