// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Discovery
{
    /// <summary>Servant class that implements the Slice interface Ice::Discovery::Lookup using the local
    /// LocatorRegistry servant.</summary>
    internal class Lookup : IAsyncLookup
    {
        private readonly string _domainId;
        private readonly ILogger _logger;
        private readonly LocatorRegistry _registryService;

        public async ValueTask FindAdapterByIdAsync(
            string domainId,
            string adapterId,
            IFindAdapterByIdReplyPrx reply,
            Current current,
            CancellationToken cancel)
        {
            if (domainId != _domainId)
            {
                return; // Ignore
            }

            (IServicePrx? proxy, bool isReplicaGroup) = _registryService.FindAdapter(adapterId);
            if (proxy != null)
            {
                // Reply to the multicast request using the given proxy.
                try
                {
                    reply = reply.Clone(preferNonSecure: NonSecure.Always);
                    await reply.FoundAdapterByIdAsync(adapterId, proxy, isReplicaGroup, cancel: cancel).
                        ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogFoundAdapterByIdRequestFailed(reply, ex);
                    }
                }
            }
        }

        public async ValueTask FindObjectByIdAsync(
            string domainId,
            Identity id,
            IFindObjectByIdReplyPrx reply,
            Current current,
            CancellationToken cancel)
        {
            if (domainId != _domainId)
            {
                return; // Ignore
            }

            if (await _registryService.FindObjectAsync(id.ToString(), cancel).ConfigureAwait(false)
                is IServicePrx proxy)
            {
                // Reply to the multicast request using the given proxy.
                try
                {
                    reply = reply.Clone(preferNonSecure: NonSecure.Always);
                    await reply.FoundObjectByIdAsync(id, proxy, cancel: cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogFoundObjectByIdRequestFailed(reply, ex);
                    }
                }
            }
        }

        internal Lookup(LocatorRegistry registryService, Communicator communicator)
        {
            _registryService = registryService;
            _domainId = communicator.GetProperty("Ice.Discovery.DomainId") ?? "";
            _logger = communicator.Logger;
        }
    }
}
