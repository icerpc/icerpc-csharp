// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class LogMultiplexedNetworkConnectionDecorator :
        LogNetworkConnectionDecorator,
        IMultiplexedNetworkConnection
    {
        private readonly IMultiplexedNetworkConnection _decoratee;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
            new LogMultiplexedStreamDecorator(
                await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false),
                Logger);

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            new LogMultiplexedStreamDecorator(_decoratee.CreateStream(bidirectional), Logger);

        internal static IMultiplexedNetworkConnection Decorate(
            IMultiplexedNetworkConnection decoratee,
            Endpoint endpoint,
            bool isServer,
            ILogger logger) =>
            new LogMultiplexedNetworkConnectionDecorator(decoratee, endpoint, isServer, logger);

        private LogMultiplexedNetworkConnectionDecorator(
            IMultiplexedNetworkConnection decoratee,
            Endpoint endpoint,
            bool isServer,
            ILogger logger)
            : base(decoratee, endpoint, isServer, logger) => _decoratee = decoratee;
    }

    // TODO: support PipeReader/PipeWriter, shutdown and dispose logging
    internal sealed class LogMultiplexedStreamDecorator : IMultiplexedStream
    {
        public long Id => _decoratee.Id;
        public PipeReader Input => _decoratee.Input;
        public bool IsBidirectional => _decoratee.IsBidirectional;
        public bool IsStarted => _decoratee.IsStarted;
        public PipeWriter Output => _decoratee.Output;
        public Action? ShutdownAction
        {
            get => _decoratee.ShutdownAction;
            set => _decoratee.ShutdownAction = value;
        }

        private readonly IMultiplexedStream _decoratee;
        private readonly ILogger _logger;

        public void Dispose() => _decoratee.Dispose();

        public Task WaitForShutdownAsync(CancellationToken cancel) => _decoratee.WaitForShutdownAsync(cancel);

        public override string? ToString() => _decoratee.ToString();

        internal LogMultiplexedStreamDecorator(IMultiplexedStream decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
