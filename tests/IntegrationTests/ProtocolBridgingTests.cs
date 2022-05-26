// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.RequestContext;
using IceRpc.Slice;
using IceRpc.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.IntegrationTests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class ProtocolBridgingTests
{
    [Test]
    public async Task ProtocolBridging_Forward(
        [Values("ice", "icerpc")] string forwarderProtocol,
        [Values("ice", "icerpc")] string targetProtocol)
    {
        var router = new Router();
        router.UseRequestContext();

        // We need to use the same coloc transport everywhere for connections to work, the test creates two
        // servers that use the same coloc transport, each with a different host.
        var coloc = new ColocTransport();
        await using ServiceProvider targetServiceProvider = new ServiceCollection()
            .AddSingleton(coloc)
            .AddIceRpcConnectionPool()
            .AddColocTest(router, Protocol.FromString(targetProtocol), "colochost1")
            .AddSingleton<IInvoker>(
                serviceProvider => new Pipeline()
                    .UseBinder(serviceProvider.GetRequiredService<IClientConnectionProvider>())
                    .UseRequestContext())
            .BuildServiceProvider();

        await using ServiceProvider forwarderServiceProvider = new ServiceCollection()
            .AddSingleton(coloc)
            .AddIceRpcConnectionPool()
            .AddColocTest(router, Protocol.FromString(forwarderProtocol), "colochost2")
            .AddSingleton<IInvoker>(
                serviceProvider => new Pipeline()
                    .UseBinder(serviceProvider.GetRequiredService<IClientConnectionProvider>())
                    .UseRequestContext())
            .BuildServiceProvider();

        Server targetServer = targetServiceProvider.GetRequiredService<Server>();
        var targetServicePrx = ProtocolBridgingTestPrx.Parse($"{targetServer.Endpoint.Protocol}:/target");
        targetServicePrx.Proxy.Endpoint = targetServer.Endpoint;
        targetServicePrx.Proxy.Invoker = targetServiceProvider.GetRequiredService<IInvoker>();
        targetServer.Listen();

        Server forwarderServer = forwarderServiceProvider.GetRequiredService<Server>();
        var forwarderServicePrx = ProtocolBridgingTestPrx.Parse($"{forwarderServer.Endpoint.Protocol}:/forward");
        forwarderServicePrx.Proxy.Endpoint = forwarderServer.Endpoint;
        forwarderServicePrx.Proxy.Invoker = forwarderServiceProvider.GetRequiredService<IInvoker>();
        forwarderServer.Listen();

        var targetService = new ProtocolBridgingTest();
        router.UseDispatchInformation();
        router.Map("/target", targetService);
        router.Map("/forward", new Forwarder(targetServicePrx.Proxy));

        // TODO: test with the other encoding; currently, the encoding is always the encoding of
        // forwardService.Proxy.Proxy

        ProtocolBridgingTestPrx newPrx = await TestProxyAsync(forwarderServicePrx, direct: false);
        Assert.That((object)newPrx.Proxy.Protocol.Name, Is.EqualTo(targetProtocol));
        _ = await TestProxyAsync(newPrx, direct: true);

        async Task<ProtocolBridgingTestPrx> TestProxyAsync(ProtocolBridgingTestPrx prx, bool direct)
        {
            var expectedPath = direct ? "/target" : "/forward";
            Assert.That(prx.Proxy.Path, Is.EqualTo(expectedPath));
            Assert.That(await prx.OpAsync(13), Is.EqualTo(13));
            IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
                new RequestContextFeature
                {
                    Value = new Dictionary<string, string> { ["MyCtx"] = "hello" }
                });

            await prx.OpContextAsync(features);
            Assert.That(features.Get<IRequestContextFeature>()?.Value, Is.EqualTo(targetService.Context));

            targetService.Context = ImmutableDictionary<string, string>.Empty;

            await prx.OpVoidAsync();

            await prx.OpOnewayAsync(42);

            Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());

            var dispatchException = Assert.ThrowsAsync<DispatchException>(
                () => prx.OpServiceNotFoundExceptionAsync());

            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
            Assert.That(dispatchException!.Origin, Is.Not.Null);

            ProtocolBridgingTestPrx newProxy = await prx.OpNewProxyAsync();
            return newProxy;
        }
    }

    internal class ProtocolBridgingTest : Service, IProtocolBridgingTest
    {
        public ImmutableDictionary<string, string> Context { get; set; } = ImmutableDictionary<string, string>.Empty;

        public ValueTask<int> OpAsync(int x, IFeatureCollection features, CancellationToken cancel) =>
            new(x);

        public ValueTask OpContextAsync(IFeatureCollection features, CancellationToken cancel)
        {
            Context = features.Get<IRequestContextFeature>()?.Value?.ToImmutableDictionary() ??
                ImmutableDictionary<string, string>.Empty;
            return default;
        }
        public ValueTask OpExceptionAsync(IFeatureCollection features, CancellationToken cancel) =>
            throw new ProtocolBridgingException(42);

        public ValueTask<ProtocolBridgingTestPrx> OpNewProxyAsync(IFeatureCollection features, CancellationToken cancel)
        {
            IDispatchInformationFeature dispatchInformation = features.Get<IDispatchInformationFeature>()!;

            var proxy = new Proxy(dispatchInformation.Connection.Protocol) { Path = dispatchInformation.Path };

            // TODO: revisit this code, add comment explaining what we are doing
            if (dispatchInformation.Connection is Connection connection)
            {
                proxy.Endpoint = connection.Endpoint;
            }
            return new(new ProtocolBridgingTestPrx(proxy));
        }

        public ValueTask OpOnewayAsync(int x, IFeatureCollection features, CancellationToken cancel) => default;

        public ValueTask OpServiceNotFoundExceptionAsync(IFeatureCollection features, CancellationToken cancel) =>
            throw new DispatchException(DispatchErrorCode.ServiceNotFound);

        public ValueTask OpVoidAsync(IFeatureCollection features, CancellationToken cancel) => default;
    }

    public sealed class Forwarder : IDispatcher
    {
        private readonly Proxy _target;

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(
            IncomingRequest incomingRequest,
            CancellationToken cancel)
        {
            // First create an outgoing request to _target from the incoming request:

            Protocol targetProtocol = _target.Protocol;

            var outgoingRequest = new OutgoingRequest(_target)
            {
                IsOneway = incomingRequest.IsOneway,
                Operation = incomingRequest.Operation,
                Payload = incomingRequest.Payload,
                Features = incomingRequest.Features,
            };

            // Then invoke

            IncomingResponse incomingResponse = await _target.Invoker!.InvokeAsync(outgoingRequest, cancel);

            // Then create an outgoing response from the incoming response.

            // When ResultType == Failure and the protocols are different, we need to transcode the exception
            // (typically a dispatch exception). Fortunately, we can simply decode it and throw it.
            if (incomingRequest.Protocol != incomingResponse.Protocol &&
                incomingResponse.ResultType == ResultType.Failure)
            {
                RemoteException remoteException = await incomingResponse.DecodeFailureAsync(
                    outgoingRequest,
                    cancel: cancel);
                remoteException.ConvertToUnhandled = false;
                throw remoteException;
            }

            // Don't forward RetryPolicy
            var fields = new Dictionary<ResponseFieldKey, OutgoingFieldValue>(
                    incomingResponse.Fields.Select(
                        pair => new KeyValuePair<ResponseFieldKey, OutgoingFieldValue>(
                            pair.Key,
                            new OutgoingFieldValue(pair.Value))));
            _ = fields.Remove(ResponseFieldKey.RetryPolicy);

            return new OutgoingResponse(incomingRequest)
            {
                Fields = fields,
                Payload = incomingResponse.Payload,
                ResultType = incomingResponse.ResultType
            };
        }

        internal Forwarder(Proxy target) => _target = target;
    }
}
