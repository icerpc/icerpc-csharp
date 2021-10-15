// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Threading.Channels;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport
    {
        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint)
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

        /// <summary>Constructs a colocated server transport.</summary>
        public ColocClientTransport()
        {
        }
    }
}
