// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object Frame, bool Fin)>;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport
    {
        private readonly MultiStreamOptions _multiStreamOptions;

        ITransportConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }

            if (ColocListener.TryGetValue(remoteEndpoint, out ColocListener? listener))
            {
                ILogger logger = loggerFactory.CreateLogger("IceRpc");
                (ColocChannelReader reader, ColocChannelWriter writer, long id) = listener.NewClientConnection();
                return new ColocConnection(
                    remoteEndpoint,
                    id,
                    writer,
                    reader,
                    isServer: false,
                    _multiStreamOptions,
                    logger);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }

        /// <summary>Construct a colocated server transport.</summary>
        /// <param name="options">The transport options.</param>
        public ColocClientTransport(MultiStreamOptions options) => _multiStreamOptions = options;
    }
}
