// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class ProtocolBridgingTests
    {
        [TestCase(ProtocolCode.IceRpc, ProtocolCode.IceRpc, true)]
        [TestCase(ProtocolCode.Ice, ProtocolCode.Ice, true)]
        [TestCase(ProtocolCode.IceRpc, ProtocolCode.IceRpc, false)]
        [TestCase(ProtocolCode.Ice, ProtocolCode.Ice, false)]
        [TestCase(ProtocolCode.IceRpc, ProtocolCode.Ice, true)]
        [TestCase(ProtocolCode.Ice, ProtocolCode.IceRpc, true)]
        [TestCase(ProtocolCode.IceRpc, ProtocolCode.Ice, false)]
        [TestCase(ProtocolCode.Ice, ProtocolCode.IceRpc, false)]
        public async Task ProtocolBridging_Forward(
            ProtocolCode forwarderProtocol,
            ProtocolCode targetProtocol,
            bool colocated)
        {
            var router = new Router();

            var targetServiceCollection = new IntegrationTestServiceCollection();
            var forwarderServiceCollection = new IntegrationTestServiceCollection();

            targetServiceCollection.UseProtocol(targetProtocol).AddTransient<IDispatcher>(_ => router);
            forwarderServiceCollection.UseProtocol(forwarderProtocol).AddTransient<IDispatcher>(_ => router);
            if (colocated)
            {
                // Use the same colocated transport instance to ensure we can invoke on the proxy returned by
                // the forwarder.
                var colocTransport = new ColocTransport();
                targetServiceCollection.UseColoc(colocTransport, port: 1);
                forwarderServiceCollection.UseColoc(colocTransport, port: 2);
            }
            else
            {
                targetServiceCollection.UseTransport("tcp");
                forwarderServiceCollection.UseTransport("tcp");
            }

            targetServiceCollection.AddTransient<IInvoker>(serviceProvider =>
                new Pipeline().UseBinder(serviceProvider.GetRequiredService<ConnectionPool>()));
            forwarderServiceCollection.AddTransient<IInvoker>(serviceProvider =>
                new Pipeline().UseBinder(serviceProvider.GetRequiredService<ConnectionPool>()));

            await using ServiceProvider targetServiceProvider = targetServiceCollection.BuildServiceProvider();
            await using ServiceProvider forwarderServiceProvider = forwarderServiceCollection.BuildServiceProvider();

            // TODO: add context testing

            Server targetServer = targetServiceProvider.GetRequiredService<Server>();
            var targetServicePrx = ProtocolBridgingTestPrx.FromPath("/target", targetServer.Protocol);
            targetServicePrx.Proxy.Endpoint = targetServer.Endpoint;
            targetServicePrx.Proxy.Invoker = targetServiceProvider.GetRequiredService<IInvoker>();

            Server forwarderServer = forwarderServiceProvider.GetRequiredService<Server>();
            var forwarderServicePrx = ProtocolBridgingTestPrx.FromPath("/forward", forwarderServer.Protocol);
            forwarderServicePrx.Proxy.Endpoint = forwarderServer.Endpoint;
            forwarderServicePrx.Proxy.Invoker = forwarderServiceProvider.GetRequiredService<IInvoker>();

            var targetService = new ProtocolBridgingTest();
            router.Map("/target", targetService);
            router.Map("/forward", new Forwarder(targetServicePrx.Proxy));

            // TODO: test with the other encoding; currently, the encoding is always the encoding of
            // forwardService.Proxy.Protocol

            ProtocolBridgingTestPrx newPrx = await TestProxyAsync(forwarderServicePrx, direct: false);
            Assert.AreEqual(targetProtocol, newPrx.Proxy.Protocol.Code);
            _ = await TestProxyAsync(newPrx, direct: true);

            async Task<ProtocolBridgingTestPrx> TestProxyAsync(ProtocolBridgingTestPrx prx, bool direct)
            {
                Assert.AreEqual(prx.Proxy.Path, direct ? "/target" : "/forward");

                Assert.AreEqual(13, await prx.OpAsync(13));

                var invocation = new Invocation
                {
                    Context = new Dictionary<string, string> { ["MyCtx"] = "hello" }
                };
                await prx.OpContextAsync(invocation);
                CollectionAssert.AreEqual(invocation.Context, targetService.Context);
                targetService.Context = ImmutableDictionary<string, string>.Empty;

                await prx.OpVoidAsync();

                await prx.OpOnewayAsync(42);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());

                ServiceNotFoundException? exception = Assert.ThrowsAsync<ServiceNotFoundException>(
                    async () => await prx.OpServiceNotFoundExceptionAsync());

                // Verifies the exception is correctly populated:
                Assert.AreEqual("/target", exception!.Origin.Path);
                Assert.AreEqual("opServiceNotFoundException", exception.Origin.Operation);

                ProtocolBridgingTestPrx newProxy = await prx.OpNewProxyAsync();
                return newProxy;
            }
        }

        internal class ProtocolBridgingTest : Service, IProtocolBridgingTest
        {
            public ImmutableDictionary<string, string> Context { get; set; } = ImmutableDictionary<string, string>.Empty;

            public ValueTask<int> OpAsync(int x, Dispatch dispatch, CancellationToken cancel) =>
                new(x);

            public ValueTask OpContextAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Context = dispatch.Context.ToImmutableDictionary();
                return default;
            }
            public ValueTask OpExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<ProtocolBridgingTestPrx> OpNewProxyAsync(Dispatch dispatch, CancellationToken cancel)
            {
                var proxy = Proxy.FromPath(dispatch.Path, dispatch.Protocol);
                proxy.Endpoint = dispatch.Connection.NetworkConnectionInformation?.LocalEndpoint;
                proxy.Encoding = dispatch.Encoding; // use the request's encoding instead of the server's encoding.
                return new(new ProtocolBridgingTestPrx(proxy));
            }

            public ValueTask OpOnewayAsync(int x, Dispatch dispatch, CancellationToken cancel) => default;

            public ValueTask OpServiceNotFoundExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ServiceNotFoundException();

            public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;
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

                // Fields and context forwarding
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields =
                    ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
                FeatureCollection features = FeatureCollection.Empty;

                if (incomingRequest.Protocol == Protocol.IceRpc && targetProtocol == Protocol.IceRpc)
                {
                    // The context is just another field, features remain empty
                    fields = incomingRequest.Fields;
                }
                else
                {
                    // When Protocol or targetProtocol is Ice, fields remains empty and we put only the request context
                    // in the initial features of the new outgoing request
                    features = features.WithContext(incomingRequest.Features.GetContext());
                }

                var outgoingRequest = new OutgoingRequest(_target, incomingRequest.Operation)
                {
                    Deadline = incomingRequest.Deadline,
                    Features = features,
                    FieldsDefaults = fields,
                    IsOneway = incomingRequest.IsOneway,
                    IsIdempotent = incomingRequest.IsIdempotent,
                    PayloadEncoding = incomingRequest.PayloadEncoding,
                    PayloadSource = incomingRequest.Payload
                };

                // Then invoke

                IncomingResponse incomingResponse = await _target.Invoker!.InvokeAsync(outgoingRequest, cancel);

                // Then create an outgoing response from the incoming response

                return new OutgoingResponse(incomingRequest)
                {
                    // Don't forward RetryPolicy
                    FieldsDefaults = incomingResponse.Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                    PayloadEncoding = incomingResponse.PayloadEncoding,
                    PayloadSource = incomingResponse.Payload,
                    ResultType = incomingResponse.ResultType
                };
            }

            internal Forwarder(Proxy target) => _target = target;
        }
    }
}
