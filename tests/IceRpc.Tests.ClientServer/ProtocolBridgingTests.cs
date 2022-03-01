// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
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
        [TestCase("icerpc", "icerpc", true)]
        [TestCase("ice", "ice", true)]
        [TestCase("icerpc", "icerpc", false)]
        [TestCase("ice", "ice", false)]
        [TestCase("icerpc", "ice", true)]
        [TestCase("ice", "icerpc", true)]
        [TestCase("icerpc", "ice", false)]
        [TestCase("ice", "icerpc", false)]
        public async Task ProtocolBridging_Forward(string forwarderProtocol, string targetProtocol, bool colocated)
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
            var targetServicePrx = ProtocolBridgingTestPrx.Parse($"{targetServer.Endpoint.Protocol}:/target");
            targetServicePrx.Proxy.Endpoint = targetServer.Endpoint;
            targetServicePrx.Proxy.Invoker = targetServiceProvider.GetRequiredService<IInvoker>();

            Server forwarderServer = forwarderServiceProvider.GetRequiredService<Server>();
            var forwarderServicePrx = ProtocolBridgingTestPrx.Parse($"{forwarderServer.Endpoint.Protocol}:/forward");
            forwarderServicePrx.Proxy.Endpoint = forwarderServer.Endpoint;
            forwarderServicePrx.Proxy.Invoker = forwarderServiceProvider.GetRequiredService<IInvoker>();

            var targetService = new ProtocolBridgingTest();
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

                var invocation = new Invocation
                {
                    Features = new FeatureCollection().WithContext(
                        new Dictionary<string, string> { ["MyCtx"] = "hello" })
                };
                await prx.OpContextAsync(invocation);
                Assert.That(invocation.Features.GetContext(), Is.EqualTo(targetService.Context));
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

            public ValueTask<int> OpAsync(int x, Dispatch dispatch, CancellationToken cancel) =>
                new(x);

            public ValueTask OpContextAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Context = dispatch.Features.GetContext().ToImmutableDictionary();
                return default;
            }
            public ValueTask OpExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<ProtocolBridgingTestPrx> OpNewProxyAsync(Dispatch dispatch, CancellationToken cancel)
            {
                var proxy = new Proxy(dispatch.Protocol) { Path = dispatch.Path };
                proxy.Endpoint = dispatch.Connection.NetworkConnectionInformation?.LocalEndpoint;
                proxy.Encoding = dispatch.Encoding; // use the request's encoding instead of the server's encoding.
                return new(new ProtocolBridgingTestPrx(proxy));
            }

            public ValueTask OpOnewayAsync(int x, Dispatch dispatch, CancellationToken cancel) => default;

            public ValueTask OpServiceNotFoundExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new DispatchException(DispatchErrorCode.ServiceNotFound);

            public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        public sealed class Forwarder : IDispatcher
        {
            private static readonly IActivator _activator = SliceDecoder.GetActivator(typeof(Forwarder).Assembly);
            private readonly Proxy _target;

            async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(
                IncomingRequest incomingRequest,
                CancellationToken cancel)
            {
                // First create an outgoing request to _target from the incoming request:

                Protocol targetProtocol = _target.Protocol;

                // Context forwarding
                FeatureCollection features = FeatureCollection.Empty;
                if (incomingRequest.Protocol == Protocol.Ice || targetProtocol == Protocol.Ice)
                {
                    // When Protocol or targetProtocol is ice, we put the request context in the initial features of the
                    // new outgoing request to ensure it gets forwarded.
                    features = features.WithContext(incomingRequest.Features.GetContext());
                }

                var outgoingRequest = new OutgoingRequest(_target)
                {
                    Features = features,
                    Fields = incomingRequest.Fields, // mostly ignored by ice, with the exception of Idempotent
                    IsOneway = incomingRequest.IsOneway,
                    Operation = incomingRequest.Operation,
                    PayloadEncoding = incomingRequest.PayloadEncoding,
                    PayloadSource = incomingRequest.Payload
                };

                // Then invoke

                IncomingResponse incomingResponse = await _target.Invoker!.InvokeAsync(outgoingRequest, cancel);

                // Then create an outgoing response from the incoming response
                // When ResultType == Failure and the protocols are different, we need to transcode the exception
                // (typically a dispatch exception). Fortunately, we can simply throw it.

                if (incomingRequest.Protocol != incomingResponse.Protocol &&
                    incomingResponse.ResultType == ResultType.Failure)
                {
                    // TODO: need better method to decode and throw the exception
                    try
                    {
                        await incomingResponse.CheckVoidReturnValueAsync(
                            _activator,
                            hasStream: false,
                            cancel).ConfigureAwait(false);
                    }
                    catch (RemoteException ex)
                    {
                        ex.ConvertToUnhandled = false;
                        throw;
                    }
                }

                return new OutgoingResponse(incomingRequest)
                {
                    // Don't forward RetryPolicy
                    Fields = incomingResponse.Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                    PayloadSource = incomingResponse.Payload,
                    ResultType = incomingResponse.ResultType
                };
            }

            internal Forwarder(Proxy target) => _target = target;
        }
    }
}
