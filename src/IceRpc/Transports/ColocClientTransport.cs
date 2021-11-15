// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILogger logger)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }

            if (ColocListener.TryGetValue(remoteEndpoint, out ColocListener? listener))
            {
                ChannelReader<ReadOnlyMemory<byte>> reader;
                ChannelWriter<ReadOnlyMemory<byte>> writer;
                (reader, writer) = listener.NewClientConnection();
                return new ColocNetworkConnection(remoteEndpoint, isServer: false, writer, reader);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }
    }
}
