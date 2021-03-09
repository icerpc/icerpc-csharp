// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Interop.ZeroC.Ice.Discovery
{
    /// <summary>Service class that implements the Slice interface Ice::Locator.</summary>
    internal class Locator : IAsyncLocator
    {
        internal ILocatorPrx Proxy { get; }

        private readonly string _domainId;
        private readonly int _latencyMultiplier;

        private readonly Server _locatorServer;

        private readonly ILookupPrx _lookup;

        // The key is a single-endpoint datagram Lookup proxy extracted from the _lookup proxy.
        // The value is a dummy datagram proxy with usually a single endpoint that is one of _replyServer's endpoints
        // and that matches the interface of the key's endpoint.
        private readonly Dictionary<ILookupPrx, IServicePrx> _lookups = new();

        private readonly Server _multicastServer;

        private readonly ILocatorRegistryPrx _registry;

        private readonly Server _replyServer;
        private readonly int _retryCount;
        private readonly TimeSpan _timeout;

        public async ValueTask<IServicePrx?> FindAdapterByIdAsync(
            string adapterId,
            Current current,
            CancellationToken cancel)
        {
            using var replyService = new FindAdapterByIdReply(_replyServer);
            return await InvokeAsync(
                (lookup, dummyReply) =>
                {
                    IFindAdapterByIdReplyPrx reply =
                        IFindAdapterByIdReplyPrx.Factory.Clone(dummyReply, path: replyService.Path);

                    return lookup.FindAdapterByIdAsync(_domainId,
                                                      adapterId,
                                                      reply,
                                                      cancel: cancel);
                },
                replyService).ConfigureAwait(false);
        }

        public async ValueTask<IServicePrx?> FindObjectByIdAsync(
            Identity identity,
            Current current,
            CancellationToken cancel)
        {
            using var replyService = new FindObjectByIdReply(_replyServer);
            return await InvokeAsync(
                (lookup, dummyReply) =>
                {
                    IFindObjectByIdReplyPrx reply =
                        IFindObjectByIdReplyPrx.Factory.Clone(dummyReply, path: replyService.Path);

                    return lookup.FindObjectByIdAsync(_domainId, identity, reply, cancel: cancel);
                },
                replyService).ConfigureAwait(false);
        }

        public ValueTask<ILocatorRegistryPrx?> GetRegistryAsync(Current current, CancellationToken cancel) =>
            new(_registry);

        internal Locator(Communicator communicator, DiscoveryServerOptions options)
        {
            if (options.ColocationScope == ColocationScope.None)
            {
                throw new ArgumentException("options.ColocationScope cannot be set to None", nameof(options));
            }

            _timeout = options.Timeout;
            if (_timeout == Timeout.InfiniteTimeSpan)
            {
                _timeout = TimeSpan.FromMilliseconds(300);
            }

            _retryCount = options.RetryCount;

            _latencyMultiplier = options.LatencyMultiplier;
            if (_latencyMultiplier < 1)
            {
                throw new ArgumentException(
                    "the value of options.LatencyMultiplier must be an integer greater than 0", nameof(options));
            }

            _domainId = options.DomainId;

            string lookupEndpoints = options.Lookup;
            if (lookupEndpoints.Length == 0)
            {
                var endpoints = new List<string>();
                List<string> ipv4Interfaces = Network.GetInterfacesForMulticast("0.0.0.0", Network.EnableIPv4);
                List<string> ipv6Interfaces = Network.GetInterfacesForMulticast("::0", Network.EnableIPv6);

                endpoints.AddRange(ipv4Interfaces.Select(
                    i => $"{DiscoveryServerOptions.DefaultIPv4Endpoint} --interface \"{i}\""));

                endpoints.AddRange(ipv6Interfaces.Select(
                    i => $"{DiscoveryServerOptions.DefaultIPv6Endpoint} --interface \"{i}\""));

                lookupEndpoints = string.Join(":", endpoints);
            }

            // Datagram proxies do not support SSL/TLS so they can only be used with PreferNonSecure set to
            // NonSecure.Always.
            _lookup = ILookupPrx.Parse($"IceDiscovery/Lookup -d:{lookupEndpoints}", communicator).Clone(
                invocationTimeout: _timeout,
                preferNonSecure: NonSecure.Always);

            _locatorServer = new Server(communicator,
                                                 new()
                                                 {
                                                     ColocationScope = options.ColocationScope,
                                                     Protocol = Protocol.Ice1
                                                 });
            Proxy = _locatorServer.Add($"{_domainId}/discovery", this, ILocatorPrx.Factory);

            // Setup locator registry.
            var registryService = new LocatorRegistry(communicator);
            _registry = _locatorServer.AddWithUUID(registryService, ILocatorRegistryPrx.Factory);

            _multicastServer = new Server(communicator,
                                                  new()
                                                  {
                                                      AcceptNonSecure = NonSecure.Always,
                                                      ColocationScope = options.ColocationScope,
                                                      Endpoints = options.MulticastEndpoints,
                                                      Name = "Discovery.Multicast",
                                                  });

            _replyServer = new Server(communicator,
                                              new()
                                              {
                                                  AcceptNonSecure = NonSecure.Always,
                                                  ColocationScope = options.ColocationScope,
                                                  Endpoints = options.ReplyEndpoints,
                                                  Name = "Discovery.Reply",
                                                  PublishedHost = options.ReplyPublishedHost
                                              });

            // Dummy proxy for replies which can have multiple endpoints (but see below).
            IServicePrx lookupReply = IServicePrx.Factory.Create(_replyServer, "dummy");

            // Create one lookup proxy per endpoint from the given proxy. We want to send a multicast datagram on
            // each of the lookup proxy.
            // TODO: this code is incorrect now that the default published endpoints are no longer an expansion
            // of the server endpoints.
            foreach (Endpoint endpoint in _lookup.Endpoints)
            {
                if (!endpoint.IsDatagram)
                {
                    throw new ArgumentException("options.Lookup can only have udp endpoints", nameof(options));
                }

                ILookupPrx key = _lookup.Clone(endpoints: ImmutableArray.Create(endpoint));
                if (endpoint["interface"] is string mcastInterface && mcastInterface.Length > 0)
                {
                    Endpoint? q = lookupReply.Endpoints.FirstOrDefault(e => e.Host == mcastInterface);
                    if (q != null)
                    {
                        _lookups[key] = lookupReply.Clone(endpoints: ImmutableArray.Create(q));
                    }
                }

                if (!_lookups.ContainsKey(key))
                {
                    // Fallback: just use the given lookup reply proxy if no matching endpoint found.
                    _lookups[key] = lookupReply;
                }
            }
            Debug.Assert(_lookups.Count > 0);

            // Add lookup Ice object
            _multicastServer.Add("IceDiscovery/Lookup", new Lookup(registryService, communicator));
        }

        internal Task ActivateAsync(CancellationToken cancel) =>
            Task.WhenAll(_locatorServer.ActivateAsync(cancel),
                         _multicastServer.ActivateAsync(cancel),
                         _replyServer.ActivateAsync(cancel));

        internal Task ShutdownAsync() =>
            Task.WhenAll(_locatorServer.ShutdownAsync(),
                         _multicastServer.ShutdownAsync(),
                         _replyServer.ShutdownAsync());

        /// <summary>Invokes a find or resolve request on a Lookup object and processes the reply(ies).</summary>
        /// <param name="findAsync">A delegate that performs the remote call. Its parameters correspond to an entry in
        /// the _lookups dictionary.</param>
        /// <param name="replyService">The reply service.</param>
        private async Task<TResult> InvokeAsync<TResult>(
            Func<ILookupPrx, IServicePrx, Task> findAsync,
            ReplyService<TResult> replyService)
        {
            // We retry only when at least one findAsync request is sent successfully and we don't get any reply.
            // TODO: this _retryCount is really an attempt count not a retry count.
            for (int i = 0; i < _retryCount; ++i)
            {
                TimeSpan start = Time.Elapsed;

                var timeoutTask = Task.Delay(_timeout, replyService.CancellationToken);

                var sendTask = Task.WhenAll(_lookups.Select(
                    entry =>
                    {
                        try
                        {
                            return findAsync(entry.Key, entry.Value);
                        }
                        catch (Exception ex)
                        {
                            return Task.FromException(ex);
                        }
                    }));

                Task task = await Task.WhenAny(sendTask, replyService.Task, timeoutTask).ConfigureAwait(false);

                if (task == sendTask)
                {
                    if (sendTask.Status == TaskStatus.Faulted)
                    {
                        if (sendTask.Exception!.InnerExceptions.Count == _lookups.Count)
                        {
                            // All the tasks failed: log warning and return empty result (no retry)
                            if (Proxy.Communicator.DiscoveryLogger.IsEnabled(LogLevel.Error))
                            {
                                Proxy.Communicator.DiscoveryLogger.LogLookupRequestFailed(
                                    _lookup,
                                    sendTask.Exception!.InnerException!);
                            }
                            replyService.SetEmptyResult();
                            return await replyService.Task.ConfigureAwait(false);
                        }
                    }
                    // For Canceled or RanToCompletion, we assume at least one send was successful. If we're wrong,
                    // we'll timeout soon anyways.

                    task = await Task.WhenAny(replyService.Task, timeoutTask).ConfigureAwait(false);
                }

                if (task == replyService.Task)
                {
                    return await replyService.Task.ConfigureAwait(false);
                }
                else if (task.IsCanceled)
                {
                    // If the timeout was canceled we delay the completion of the request to give a chance to other
                    // members of this replica group to reply
                    return await
                        replyService.GetReplicaGroupRepliesAsync(start, _latencyMultiplier).ConfigureAwait(false);
                }
                // else timeout, so we retry until _retryCount
            }

            replyService.SetEmptyResult(); // _retryCount exceeded
            return await replyService.Task.ConfigureAwait(false);
        }
    }

    /// <summary>The base class of all Reply service that helps collect / gather the reply(ies) to a lookup request.
    /// </summary>
    internal class ReplyService<TResult> : IService, IDisposable
    {
        internal CancellationToken CancellationToken => _cancellationSource.Token;
        internal string Path { get; }

        internal Task<TResult> Task => _completionSource.Task;

        private readonly CancellationTokenSource _cancellationSource;
        private readonly TaskCompletionSource<TResult> _completionSource;
        private readonly TResult _emptyResult;

        private readonly Server _replyServer;

        public void Dispose()
        {
            _cancellationSource.Dispose();
            _replyServer.Remove(Path);
        }

        internal async Task<TResult> GetReplicaGroupRepliesAsync(TimeSpan start, int latencyMultiplier)
        {
            // This method is called by InvokeAsync after the first reply from a replica group to wait for additional
            // replies from the replica group.
            TimeSpan latency = (Time.Elapsed - start) * latencyMultiplier;
            if (latency == TimeSpan.Zero)
            {
                latency = TimeSpan.FromMilliseconds(1);
            }
            await System.Threading.Tasks.Task.Delay(latency).ConfigureAwait(false);

            SetResult(CollectReplicaReplies());
            return await Task.ConfigureAwait(false);
        }

        internal void SetEmptyResult() => _completionSource.SetResult(_emptyResult);

        private protected ReplyService(TResult emptyResult, Server replyServer)
        {
            // Add service (this) to server with new UUID path.
            Path = replyServer.AddWithUUID(this, IServicePrx.Factory).Path;

            _cancellationSource = new();
            _completionSource = new();
            _emptyResult = emptyResult;
            _replyServer = replyServer;
        }

        private protected void Cancel() => _cancellationSource.Cancel();

        private protected virtual TResult CollectReplicaReplies()
        {
            Debug.Assert(false); // must be overridden if called by WaitForReplicaGroupRepliesAsync
            return _emptyResult;
        }

        private protected void SetResult(TResult result) => _completionSource.SetResult(result);
    }

    /// <summary>Service class that implements the Slice interface FindAdapterByIdReply.</summary>
    internal sealed class FindAdapterByIdReply : ReplyService<IServicePrx?>, IAsyncFindAdapterByIdReply
    {
        private readonly object _mutex = new();
        private readonly HashSet<IServicePrx> _proxies = new();

        public ValueTask FoundAdapterByIdAsync(
            string adapterId,
            IServicePrx proxy,
            bool isReplicaGroup,
            Current current,
            CancellationToken cancel)
        {
            if (isReplicaGroup)
            {
                lock (_mutex)
                {
                    _proxies.Add(proxy);
                    if (_proxies.Count == 1)
                    {
                        // Cancel WhenAny and let InvokeAsync wait for additional replies from the replica group, and
                        // later call CollectReplicaReplies.
                        Cancel();
                    }
                }
            }
            else
            {
                SetResult(proxy);
            }
            return default;
        }

        internal FindAdapterByIdReply(Server replyServer)
            : base(emptyResult: null, replyServer)
        {
        }

        private protected override IServicePrx? CollectReplicaReplies()
        {
            lock (_mutex)
            {
                Debug.Assert(_proxies.Count > 0);
                var endpoints = new List<Endpoint>();
                IServicePrx result = _proxies.First();
                foreach (IServicePrx prx in _proxies)
                {
                    endpoints.AddRange(prx.Endpoints);
                }
                return result.Clone(endpoints: endpoints);
            }
        }
    }

    /// <summary>Service class that implements the Slice interface FindObjectByIdReply.</summary>
    internal class FindObjectByIdReply : ReplyService<IServicePrx?>, IAsyncFindObjectByIdReply
    {
        public ValueTask FoundObjectByIdAsync(Identity id, IServicePrx proxy, Current current, CancellationToken cancel)
        {
            SetResult(proxy);
            return default;
        }

        internal FindObjectByIdReply(Server replyServer)
            : base(emptyResult: null, replyServer)
        {
        }
    }
}
