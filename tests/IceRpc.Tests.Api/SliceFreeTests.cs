// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Api
{
    // These tests verify we can use IceRpc without Slice definitions.

    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    // [Log(LogAttributeLevel.Information)]
    [FixtureLifeCycle(LifeCycle.SingleInstance)]
    public sealed class SliceFreeTests : IAsyncDisposable
    {
        private const string _austin = "/austin";

        // the actual name of the payload encoding sent with the icerpc requests and responses
        private static readonly Encoding _customEncoding = Encoding.FromString("utf8");

        private const string _doingWell = "muy bien";
        private const string _joe = "/joe";
        private const string _greeting = "how are you doing?";
        private const string _notGood = "feeling under the weather";
        private const string _sayHelloOperation = "sayHello";
        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        private readonly ServiceProvider _serviceProvider;
        private readonly Proxy _proxy;

        public SliceFreeTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map(_joe, new Greeter(ResultType.Success, _doingWell));
                    router.Map(_austin, new Greeter(ResultType.Failure, _notGood));
                    return router;
                })
                .BuildServiceProvider();
            _proxy = _serviceProvider.GetRequiredService<Proxy>();
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task SliceFree_InvokeAsync()
        {
            var payload = new ReadOnlySequence<byte>(_utf8.GetBytes(_greeting));

            var joeProxy = _proxy with { Path = _joe };
            var request = new OutgoingRequest(joeProxy, _sayHelloOperation)
            {
                PayloadEncoding = _customEncoding,
                PayloadSource = PipeReader.Create(payload)
            };
            IncomingResponse response = await joeProxy.Invoker.InvokeAsync(request).ConfigureAwait(false);

            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(response.PayloadEncoding, Is.EqualTo(_customEncoding));
            string greetingResponse = _utf8.GetString((await ReadFullPayloadAsync(response.Payload)).Span);
            await response.Payload.CompleteAsync(); // done with payload
            Assert.That(greetingResponse, Is.EqualTo(_doingWell));

            var austinProxy = _proxy with { Path = _austin };
            request = new OutgoingRequest(austinProxy, _sayHelloOperation)
            {
                PayloadEncoding = _customEncoding,
                PayloadSource = PipeReader.Create(payload)
            };
            response = await austinProxy.Invoker.InvokeAsync(request);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
            Assert.That(response.PayloadEncoding, Is.EqualTo(_customEncoding));
            greetingResponse = _utf8.GetString((await ReadFullPayloadAsync(response.Payload)).Span);
            await response.Payload.CompleteAsync(); // done with payload
            Assert.That(greetingResponse, Is.EqualTo(_notGood));
        }

        [Test]
        public async Task SliceFree_ExceptionAsync()
        {
            var payload = new ReadOnlySequence<byte>(_utf8.GetBytes(_greeting));

            var badProxy = _proxy with { Path = "/bad" };
            var request = new OutgoingRequest(badProxy, _sayHelloOperation)
            {
                PayloadEncoding = _customEncoding,
                PayloadSource = PipeReader.Create(payload)
            };

            IncomingResponse response = await badProxy.Invoker.InvokeAsync(request);

            Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
            Assert.That(response.PayloadEncoding, Is.EqualTo(Encoding.Slice20));
            await response.Payload.CompleteAsync(); // done with payload
            // TODO: unfortunately there is currently no way to decode this response (2.0-encoded exception)

            var joeProxy = _proxy with { Path = _joe };
            Slice.IServicePrx slicePrx = new Slice.ServicePrx(joeProxy);

            // the greeter does not implement ice_ping since ice_ping is a Slice operation:
            Assert.ThrowsAsync<Slice.OperationNotFoundException>(async () => await slicePrx.IcePingAsync());
        }

        [Test]
        public async Task SliceFree_InvocationAsync()
        {
            var payload = new ReadOnlySequence<byte>(_utf8.GetBytes(_greeting));
            var joeProxy = _proxy with { Path = _joe };

            var request = new OutgoingRequest(joeProxy, _sayHelloOperation)
            {
                IsOneway = true,
                PayloadEncoding = _customEncoding,
                PayloadSource = PipeReader.Create(payload)
            };

            IncomingResponse response = await joeProxy.Invoker.InvokeAsync(request);

            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(response.PayloadEncoding, Is.EqualTo(_customEncoding));
            Assert.That((await ReadFullPayloadAsync(response.Payload)).IsEmpty);
            await response.Payload.CompleteAsync(); // done with payload

            // TODO: more invocation tests
        }

        private static async ValueTask<ReadOnlyMemory<byte>> ReadFullPayloadAsync(
            PipeReader reader,
            CancellationToken cancel = default)
        {
            ReadResult readResult = await reader.ReadAllAsync(cancel);

            Assert.That(readResult.Buffer.IsSingleSegment); // very likely; if not, fix test
            return readResult.Buffer.First;
        }

        private class Greeter : IDispatcher
        {
            private readonly string _message;

            private readonly ResultType _resultType;

            public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
            {
                if (request.Operation != _sayHelloOperation)
                {
                    throw new Slice.OperationNotFoundException();
                }

                Assert.That(request.PayloadEncoding, Is.EqualTo(_customEncoding));

                string greeting = _utf8.GetString((await ReadFullPayloadAsync(request.Payload, cancel)).Span);
                await request.Payload.CompleteAsync(); // done with payload

                Assert.That(greeting, Is.EqualTo(_greeting));

                var payload = new ReadOnlySequence<byte>(_utf8.GetBytes(_message));
                var response = new OutgoingResponse(request)
                {
                    PayloadSource = PipeReader.Create(payload),
                    ResultType = _resultType
                    // use same payload encoding as request (default)
                };
                return response;
            }

            internal Greeter(ResultType resultType, string message)
            {
                _message = message;
                _resultType = resultType;
            }
        }
    }
}
