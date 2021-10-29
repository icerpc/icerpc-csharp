// Copyright (c) ZeroC, Inc. All rights reserved.

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

        protected NetworkConnectionInformation? Information { get; set; }

        private protected abstract INetworkConnection Decoratee { get; }

        private protected bool IsDatagram { get; set; }

        private protected bool IsServer { get; }
        private protected ILogger Logger { get; }

        private readonly Endpoint _endpoint;

        public void Dispose()
        {
            Decoratee.Dispose();

            if (Information is NetworkConnectionInformation connectionInformation)
            {
                using IDisposable? scope = Logger.StartConnectionScope(connectionInformation, IsServer);

                if (IsServer)
                {
                    Logger.LogServerConnectionClosed();
                }
                else
                {
                    Logger.LogClientConnectionClosed();
                }
            }
            // We don't emit a log when closing a connection that was not connected.

        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => Decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => Decoratee.ToString();

        internal LogNetworkConnectionDecorator(
            bool isServer,
            Endpoint endpoint,
            ILogger logger)
        {
            _endpoint = endpoint;
            IsServer = isServer;
            Logger = logger;
        }

        internal void LogReceivedData(Memory<byte> buffer)
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
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Logger.LogReceivedData(buffer.Length, sb.ToString().Trim());
        }

        internal void LogSentData(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
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
                if (size == 32 && i != buffers.Length)
                {
                    _ = sb.Append("...");
                }
            }
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Logger.LogSentData(size, sb.ToString().Trim());
        }

        private protected void LogConnected()
        {
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Action logSuccess = (IsServer, IsDatagram) switch
            {
                (false, false) => Logger.LogConnectionEstablished,
                (false, true) => Logger.LogStartSendingDatagrams,
                (true, false) => Logger.LogConnectionAccepted,
                (true, true) => Logger.LogStartReceivingDatagrams
            };
            logSuccess();
        }

        private protected void LogConnectFailed(Exception ex)
        {
            using IDisposable? scope = Logger.StartConnectionScope(_endpoint, IsServer);

            // TODO: different log messages for UDP?
            if (IsServer)
            {
                Logger.LogConnectionAcceptFailed(ex);
            }
            else
            {
                Logger.LogConnectionConnectFailed(ex);
            }
        }
    }
}
