// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object Frame, bool Fin)>;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport
    {
        MultiStreamConnection IClientTransport.CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (remoteEndpoint.ExternalParams.Count > 0 || remoteEndpoint.LocalParams.Count > 0)
            {
                throw new FormatException($"unknown parameter in endpoint {remoteEndpoint}");
            }

            if (ColocListener.TryGetValue(remoteEndpoint, out ColocListener? listener))
            {
                (ColocChannelReader reader, ColocChannelWriter writer, long id) = listener.NewClientConnection();
                return new ColocConnection(remoteEndpoint, id, writer, reader, options, logger);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }
    }
}
