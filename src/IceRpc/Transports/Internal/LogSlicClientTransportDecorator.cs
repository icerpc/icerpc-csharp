// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal class LogSlicClientTransportDecorator : LogClientTransportDecorator
    {
        internal LogSlicClientTransportDecorator(SlicClientTransport decoratee, ILogger logger)
            : base(decoratee, logger)
        {
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> factory = decoratee.SlicFrameReaderWriterFactory;
            decoratee.SlicFrameReaderWriterFactory = networkStream =>
                {
                    (ISlicFrameReader reader, ISlicFrameWriter writer) = factory(networkStream);
                    return (new LogSlicFrameReaderDecorator(reader, logger),
                            new LogSlicFrameWriterDecorator(writer, logger));
                };
        }
    }
}
