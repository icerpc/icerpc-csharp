// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal delegate T LogNetworkConnectionDecoratorFactory<T>(
        T decoratee,
        bool isServer,
        ILogger logger) where T : INetworkConnection;

    internal abstract class LogNetworkConnectionDecorator : INetworkConnection
    {
        public bool IsSecure => _decoratee.IsSecure;
        public TimeSpan LastActivity => _decoratee.LastActivity;

        internal ILogger Logger { get; }

        private protected bool IsServer { get; }

        private protected NetworkConnectionInformation? Information { get; set; }

        private readonly INetworkConnection _decoratee;

        public virtual async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            try
            {
                Information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogConnectFailed(ex);
                throw;
            }

            Logger.LogConnect(Information.Value.LocalEndpoint, Information.Value.RemoteEndpoint);
            return Information.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);

            if (Information is NetworkConnectionInformation connectionInformation)
            {
                // TODO: we start the scope here because DisposeAsync is called directly by Connection, and not
                // through a higher-level interface method such as IProtocolConnection.DisposeAsync.
                using IDisposable scope = Logger.StartConnectionScope(connectionInformation, IsServer);
                Logger.LogConnectionDispose();
            }
            // We don't emit a log when closing a connection that was not connected.
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => _decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkConnectionDecorator(INetworkConnection decoratee, bool isServer, ILogger logger)
        {
            _decoratee = decoratee;
            IsServer = isServer;
            Logger = logger;
        }

        internal static string ToHexString(Memory<byte> buffer)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(buffer.Length, 32); ++i)
            {
                _ = sb.Append("0x").Append(Convert.ToHexString(buffer.Span[i..(i + 1)])).Append(' ');
            }
            if (buffer.Length > 32)
            {
                _ = sb.Append("...");
            }
            return sb.ToString().Trim();
        }

        internal static string ToHexString(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            int size = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < buffers.Length; ++i)
            {
                ReadOnlyMemory<byte> buffer = buffers.Span[i];
                if (size < 32)
                {
                    for (int j = 0; j < Math.Min(buffer.Length, 32 - size); ++j)
                    {
                        _ = sb.Append("0x").Append(Convert.ToHexString(buffer.Span[j..(j + 1)])).Append(' ');
                    }
                }
                size += buffer.Length;
                if (size > 32)
                {
                    _ = sb.Append("...");
                    break;
                }
            }
            return sb.ToString().Trim();
        }
    }
}
