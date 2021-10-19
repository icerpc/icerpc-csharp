// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal class LogSlicServerTransportDecorator : LogServerTransportDecorator
    {
        internal LogSlicServerTransportDecorator(SimpleServerTransport decoratee, ILogger logger)
            : base(decoratee, logger)
        {
            Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> factory = decoratee.SlicFrameReaderWriterFactory;
            decoratee.SlicFrameReaderWriterFactory = simpleStream =>
                {
                    (ISlicFrameReader reader, ISlicFrameWriter writer) = factory(simpleStream);
                    return (new LogSlicFrameReaderDecorator(reader, logger),
                            new LogSlicFrameWriterDecorator(writer, logger));
                };
        }
    }
}
