// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport
    {
        private readonly MultiStreamOptions _options;

        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
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
                ILogger logger = loggerFactory.CreateLogger("IceRpc.Transports");
                return LogNetworkConnectionDecorator.Create(
                    new ColocConnection(remoteEndpoint, isServer: false, _options, writer, reader, logger),
                    logger);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }

        /// <summary>Construct a colocated server transport.</summary>
        /// <param name="options">The transport options.</param>
        public ColocClientTransport(MultiStreamOptions options) => _options = options;
    }
}
