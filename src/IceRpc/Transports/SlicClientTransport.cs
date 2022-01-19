// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
    /// client transport.</summary>
    public class SlicClientTransport : IClientTransport<IMultiplexedNetworkConnection>
    {
        private static readonly Func<ISlicFrameReader, ISlicFrameReader> _defaultSlicFrameReaderDecorator =
            reader => reader;
        private static readonly Func<ISlicFrameWriter, ISlicFrameWriter> _defaultSlicFrameWriterDecorator =
            writer => writer;
        private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;
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
            _slicOptions = slicOptions;
        }

        IMultiplexedNetworkConnection IClientTransport<IMultiplexedNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILogger logger)
        {
            // This is the composition root of the Slic client transport, where we install log decorators when logging
            // is enabled.

            ISimpleNetworkConnection simpleNetworkConnection =
                _simpleClientTransport.CreateConnection(remoteEndpoint, logger);

            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator = _defaultSlicFrameReaderDecorator;
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator = _defaultSlicFrameWriterDecorator;

            if (logger.IsEnabled(LogLevel.Error))
            {
                // TODO: should we add a log decorator over simple network connection too?

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
