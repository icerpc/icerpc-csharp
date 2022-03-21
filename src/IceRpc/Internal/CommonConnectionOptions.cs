// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;

namespace IceRpc.Internal
{
    /// <summary>A helper struct that delegates to either an underlying <see cref="ConnectionOptions"/> or
    /// to an underlying <see cref="ServerOptions"/>.</summary>
    internal readonly struct CommonConnectionOptions
    {
        internal TimeSpan ConnectTimeout => _connectionOptions?.ConnectTimeout ?? _serverOptions!.ConnectTimeout;
        internal IDispatcher Dispatcher => _connectionOptions?.Dispatcher ?? _serverOptions!.Dispatcher;

        internal IDictionary<ConnectionFieldKey, OutgoingFieldValue> Fields =>
            _connectionOptions?.Fields ?? _serverOptions!.Fields;

        internal IceProtocolOptions? IceProtocolOptions =>
            _connectionOptions?.IceProtocolOptions ?? _serverOptions?.IceProtocolOptions;

        internal bool KeepAlive => _connectionOptions?.KeepAlive ?? _serverOptions!.KeepAlive;

        private readonly ConnectionOptions? _connectionOptions;
        private readonly ServerOptions? _serverOptions;

        internal CommonConnectionOptions(ConnectionOptions connectionOptions)
        {
            _connectionOptions = connectionOptions;
            _serverOptions = null;
        }

        internal CommonConnectionOptions(ServerOptions serverOptions)
        {
            _serverOptions = serverOptions;
            _connectionOptions = null;
        }
    }
}
