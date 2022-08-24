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
        var forwarderServerAddress = new ServerAddress(new Uri($"{forwarderProtocol}://colochost1"));
        var targetServerAddress = new ServerAddress(new Uri($"{targetProtocol}://colochost2"));

        var forwarderServiceProxy = new ProtocolBridgingTestProxy
        {
            ServiceAddress = new(new Uri($"{forwarderServerAddress}forward"))
        };

        var targetServiceProxy = new ProtocolBridgingTestProxy
        {
            ServiceAddress = new(new Uri($"{targetServerAddress}target"))
        };

        var targetService = new ProtocolBridgingTest(targetServerAddress);

        IServiceCollection services = new ServiceCollection()
            .AddColocTransport()
            .AddIceRpcConnectionCache()
            .AddSingleton<IProtocolBridgingTest>(targetService)
            .AddSingleton(_ => new Forwarder(targetServiceProxy.ToProxy<ServiceProxy>()))
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
                    .UseRequestContext()
                    .Into<ConnectionCache>());

        services.AddOptions<ServerOptions>("forwarder").Configure(options => options.ServerAddress = forwarderServerAddress);
        services.AddOptions<ServerOptions>("target").Configure(options => options.ServerAddress = targetServerAddress);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);

        forwarderServiceProxy = forwarderServiceProxy with { Invoker = serviceProvider.GetRequiredService<IInvoker>() };
        targetServiceProxy = targetServiceProxy with { Invoker = serviceProvider.GetRequiredService<IInvoker>() };

        foreach (Server server in serviceProvider.GetServices<Server>())
        {
            server.Listen();
        }

        // TODO: test with the other encoding; currently, the encoding is always slice2

        ProtocolBridgingTestProxy newProxy = await TestProxyAsync(forwarderServiceProxy, direct: false);
        Assert.That((object)newProxy.ServiceAddress.Protocol!.Name, Is.EqualTo(targetProtocol));
        _ = await TestProxyAsync(newProxy, direct: true);

        foreach (Server server in serviceProvider.GetServices<Server>())
        {
            await server.ShutdownAsync();
        }

        async Task<ProtocolBridgingTestProxy> TestProxyAsync(ProtocolBridgingTestProxy proxy, bool direct)
        {
            var expectedPath = direct ? "/target" : "/forward";
            Assert.That(proxy.ServiceAddress.Path, Is.EqualTo(expectedPath));
            Assert.That(await proxy.OpAsync(13), Is.EqualTo(13));
            IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
                new RequestContextFeature
                {
                    Value = new Dictionary<string, string> { ["MyCtx"] = "hello" }
                });

            await proxy.OpContextAsync(features);
            Assert.That(features.Get<IRequestContextFeature>()?.Value, Is.EqualTo(targetService.Context));

            targetService.Context = ImmutableDictionary<string, string>.Empty;

            await proxy.OpVoidAsync();

            await proxy.OpOnewayAsync(42);

            Assert.ThrowsAsync<ProtocolBridgingException>(async () => await proxy.OpExceptionAsync());

            var dispatchException = Assert.ThrowsAsync<DispatchException>(
                () => proxy.OpServiceNotFoundExceptionAsync());

            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
            Assert.That(dispatchException!.Origin, Is.Not.Null);

            ProtocolBridgingTestProxy newProxy = await proxy.OpNewProxyAsync();
            return newProxy;
        }
    }

    internal class ProtocolBridgingTest : Service, IProtocolBridgingTest
    {
        public ImmutableDictionary<string, string> Context { get; set; } = ImmutableDictionary<string, string>.Empty;

        private readonly ServerAddress _publishedServerAddress;

        public ProtocolBridgingTest(ServerAddress publishedServerAddress) => _publishedServerAddress = publishedServerAddress;

        public ValueTask<int> OpAsync(int x, IFeatureCollection features, CancellationToken cancellationToken) =>
            new(x);

        public ValueTask OpContextAsync(IFeatureCollection features, CancellationToken cancellationToken)
        {
            Context = features.Get<IRequestContextFeature>()?.Value?.ToImmutableDictionary() ??
                ImmutableDictionary<string, string>.Empty;
            return default;
        }
        public ValueTask OpExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw new ProtocolBridgingException(42);

        public ValueTask<ProtocolBridgingTestProxy> OpNewProxyAsync(IFeatureCollection features, CancellationToken cancellationToken)
        {
            IDispatchInformationFeature dispatchInformation = features.Get<IDispatchInformationFeature>()!;

            var serviceAddress = new ServiceAddress(dispatchInformation.Protocol)
            {
                Path = dispatchInformation.Path,
                ServerAddress = _publishedServerAddress
            };

            return new(new ProtocolBridgingTestProxy { ServiceAddress = serviceAddress });
        }

        public ValueTask OpOnewayAsync(int x, IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask OpServiceNotFoundExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw new DispatchException(DispatchErrorCode.ServiceNotFound);

        public ValueTask OpVoidAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }

    public sealed class Forwarder : IDispatcher
    {
        private readonly ServiceProxy _target;

        public async ValueTask<OutgoingResponse> DispatchAsync(
            IncomingRequest request,
            CancellationToken cancellationToken)
        {
            // First create an outgoing request to _target from the incoming request:

            Protocol targetProtocol = _target.ServiceAddress.Protocol!;

            var outgoingRequest = new OutgoingRequest(_target.ServiceAddress)
            {
                IsOneway = request.IsOneway,
                Operation = request.Operation,
                Payload = request.Payload,
                Features = request.Features,
            };

            // Then invoke

            IncomingResponse incomingResponse = await _target.Invoker!.InvokeAsync(outgoingRequest, cancellationToken);

            // Then create an outgoing response from the incoming response.

            // When ResultType == Failure and the protocols are different, we need to transcode the exception
            // (typically a dispatch exception). Fortunately, we can simply decode it and throw it.
            if (request.Protocol != incomingResponse.Protocol &&
                incomingResponse.ResultType == ResultType.Failure)
            {
                RemoteException remoteException = await incomingResponse.DecodeFailureAsync(
                    outgoingRequest,
                    sender: _target,
                    cancellationToken: cancellationToken);
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

            return new OutgoingResponse(request)
            {
                Fields = fields,
                Payload = incomingResponse.Payload,
                ResultType = incomingResponse.ResultType
            };
        }

        internal Forwarder(ServiceProxy target) => _target = target;
    }
}
