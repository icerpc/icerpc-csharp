// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Builder;
using IceRpc.Features;
using IceRpc.RequestContext;
using IceRpc.Slice;
using IceRpc.Tests.Common;
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
        Endpoint forwarderEndpoint = $"{forwarderProtocol}://colochost1";
        Endpoint targetEndpoint = $"{targetProtocol}://colochost2";

        var forwarderServicePrx = ProtocolBridgingTestPrx.Parse($"{forwarderProtocol}:/forward");
        forwarderServicePrx.Proxy.Endpoint = forwarderEndpoint;

        var targetServicePrx = ProtocolBridgingTestPrx.Parse($"{targetProtocol}:/target");
        targetServicePrx.Proxy.Endpoint = targetEndpoint;

        var targetService = new ProtocolBridgingTest(targetEndpoint);

        IServiceCollection services = new ServiceCollection()
            .AddColocTransport()
            .AddIceRpcConnectionPool()
            .AddSingleton<IProtocolBridgingTest>(targetService)
            .AddSingleton(_ => new Forwarder(targetServicePrx.Proxy))
            .AddIceRpcServer(
                "forwarder",
                builder => builder
                    .UseRequestContext()
                    .Map<Forwarder>("/forward"))
            .AddIceRpcServer(
                "target",
                builder => builder
                    .UseRequestContext()
                    .UseDispatchInformation()
                    .Map<IProtocolBridgingTest>("/target"))
            .AddIceRpcInvoker(
                builder => builder
                    .UseBinder()
                    .UseRequestContext());

        services.AddOptions<ServerOptions>("forwarder").Configure(options => options.Endpoint = forwarderEndpoint);
        services.AddOptions<ServerOptions>("target").Configure(options => options.Endpoint = targetEndpoint);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);

        forwarderServicePrx.Proxy.Invoker = serviceProvider.GetRequiredService<IInvoker>();
        targetServicePrx.Proxy.Invoker = serviceProvider.GetRequiredService<IInvoker>();

        foreach (Server server in serviceProvider.GetServices<Server>())
        {
            server.Listen();
        }

        // TODO: test with the other encoding; currently, the encoding is always slice2

        ProtocolBridgingTestPrx newPrx = await TestProxyAsync(forwarderServicePrx, direct: false);
        Assert.That((object)newPrx.Proxy.Protocol.Name, Is.EqualTo(targetProtocol));
        _ = await TestProxyAsync(newPrx, direct: true);

        foreach (Server server in serviceProvider.GetServices<Server>())
        {
            await server.ShutdownAsync();
        }

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

        private readonly Endpoint _publishedEndpoint;

        public ProtocolBridgingTest(Endpoint publishedEndpoint) => _publishedEndpoint = publishedEndpoint;

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

            var proxy = new Proxy(dispatchInformation.Connection.Protocol)
            {
                Path = dispatchInformation.Path,
                Endpoint = _publishedEndpoint
            };

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
