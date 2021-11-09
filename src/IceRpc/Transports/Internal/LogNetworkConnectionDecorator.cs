// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal delegate T LogNetworkConnectionDecoratorFactory<T>(
        T decoratee,
        bool isServer,
        Endpoint endpoint,
        ILogger logger) where T : INetworkConnection;

    internal abstract class LogNetworkConnectionDecorator : INetworkConnection
    {
        public bool IsSecure => Decoratee.IsSecure;
        public TimeSpan LastActivity => Decoratee.LastActivity;

        internal ILogger Logger { get; }

        private protected abstract INetworkConnection Decoratee { get; }

        private protected bool IsServer { get; }

        private protected NetworkConnectionInformation? Information { get; set; }

        public void Dispose()
        {
            Decoratee.Dispose();

            if (Information is NetworkConnectionInformation connectionInformation)
            {
                using IDisposable scope = Logger.StartConnectionScope(connectionInformation, IsServer);
                Logger.LogConnectionDispose();
            }
            // We don't emit a log when closing a connection that was not connected.
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => Decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => Decoratee.ToString();

        internal LogNetworkConnectionDecorator(bool isServer, ILogger logger)
        {
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
                }
            }
            return sb.ToString().Trim();
        }
    }
}
