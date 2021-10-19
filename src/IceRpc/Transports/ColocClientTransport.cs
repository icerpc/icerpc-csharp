// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Threading.Channels;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : SimpleClientTransport
    {
        /// <summary>Construct a colocated client transport.</summary>
        public ColocClientTransport() :
            base(new(), TimeSpan.MaxValue)
        {
        }

        /// <summary>Construct a colocated client transport.</summary>
        /// <param name="slicOptions">The Slic options.</param>
        public ColocClientTransport(SlicOptions slicOptions) :
            base(slicOptions, TimeSpan.MaxValue)
        {
        }

        /// <inheritdoc/>
        protected override SimpleNetworkConnection CreateConnection(Endpoint remoteEndpoint)
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
