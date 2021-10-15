// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    public static class InternalTestHelper
    {
        static public IClientTransport CreateSlicDecorator(
            IClientTransport clientTransport,
            SlicOptions? slicOptions = null)
        {
            ILogger logger = LogAttributeLoggerFactory.Instance.CreateLogger("IceRpc.Transports");
            return new SlicClientTransportDecorator(
                clientTransport,
                slicOptions ?? new(),
                stream => (
                    new LogSlicFrameReaderDecorator(new StreamSlicFrameReader(stream), logger),
                    new LogSlicFrameWriterDecorator(new StreamSlicFrameWriter(stream), logger)));
        }

        static public IServerTransport CreateSlicDecorator(
            IServerTransport serverTransport,
            SlicOptions? slicOptions = null)
        {
            ILogger logger = LogAttributeLoggerFactory.Instance.CreateLogger("IceRpc.Transports");
            return new SlicServerTransportDecorator(
                serverTransport,
                slicOptions ?? new(),
                stream => (
                    new LogSlicFrameReaderDecorator(new StreamSlicFrameReader(stream), logger),
                    new LogSlicFrameWriterDecorator(new StreamSlicFrameWriter(stream), logger)));
        }
    }
}
