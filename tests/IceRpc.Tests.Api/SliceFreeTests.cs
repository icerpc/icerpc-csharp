// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using NUnit.Framework;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Tests.Api
{
    // These tests verify we can use IceRpc without Slice definitions.

    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    [Log(LogAttributeLevel.Information)]
    [FixtureLifeCycle(LifeCycle.SingleInstance)]
    public sealed class SliceFreeTests : IAsyncDisposable
    {
        private const string _austin = "/austin";

        // the actual name of the payload encoding sent with the ice2 requests and responses
        private static readonly Encoding _customEncoding = Encoding.FromString("utf8");

        private const string _doingWell = "muy bien";
        private const string _joe = "/joe";
        private const string _greeting = "how are you doing?";
        private const string _notGood = "feeling under the weather";
        private const string _sayHelloOperation = "sayHello";
        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        private readonly Connection _connection;
        private readonly Server _server;

        public SliceFreeTests()
        {
            var router = new Router();
            router.Map(_joe, new Greeter(ResultType.Success, _doingWell));
            router.Map(_austin, new Greeter(ResultType.Failure, _notGood));

            _server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                LoggerFactory = LogAttributeLoggerFactory.Instance
            };

            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint,
                LoggerFactory = LogAttributeLoggerFactory.Instance
            };
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _server.DisposeAsync();
        }

        [Test]
        public async Task SliceFree_InvokeAsync()
        {
            var payload = new ReadOnlyMemory<byte>[] { _utf8.GetBytes(_greeting) };

            var joeProxy = Proxy.FromConnection(_connection, _joe);
            IncomingResponse response = await joeProxy.InvokeAsync(_sayHelloOperation, _customEncoding, payload);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(response.PayloadEncoding, Is.EqualTo(_customEncoding));
            string greetingResponse = _utf8.GetString((await ReadFullPayloadAsync(response.Payload)).Span);
            await response.Payload.CompleteAsync(); // done with payload
            Assert.That(greetingResponse, Is.EqualTo(_doingWell));

            var austinProxy = Proxy.FromConnection(_connection, _austin);
            response = await austinProxy.InvokeAsync(_sayHelloOperation, _customEncoding, payload);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
            Assert.That(response.PayloadEncoding, Is.EqualTo(_customEncoding));
            greetingResponse = _utf8.GetString((await ReadFullPayloadAsync(response.Payload)).Span);
            await response.Payload.CompleteAsync(); // done with payload
            Assert.That(greetingResponse, Is.EqualTo(_notGood));
        }

        [Test]
        public async Task SliceFree_ExceptionAsync()
        {
            var payload = new ReadOnlyMemory<byte>[] { _utf8.GetBytes(_greeting) };

            var badProxy = Proxy.FromConnection(_connection, "/bad");
            IncomingResponse response = await badProxy.InvokeAsync(_sayHelloOperation, _customEncoding, payload);

            Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
            Assert.That(response.PayloadEncoding, Is.EqualTo(Encoding.Ice20));
            await response.Payload.CompleteAsync(); // done with payload
            // TODO: unfortunately there is currently no way to decode this response (2.0-encoded exception)

            var joeProxy = Proxy.FromConnection(_connection, _joe);
            IServicePrx slicePrx = new ServicePrx(joeProxy);

            // the greeter does not implement ice_ping since ice_ping is a Slice operation:
            Assert.ThrowsAsync<OperationNotFoundException>(async () => await slicePrx.IcePingAsync());
        }

        [Test]
        public async Task SliceFree_InvocationAsync()
        {
            var payload = new ReadOnlyMemory<byte>[] { _utf8.GetBytes(_greeting) };
            var joeProxy = Proxy.FromConnection(_connection, _joe);

            var invocation = new Invocation { IsOneway = true };

            IncomingResponse response = await joeProxy.InvokeAsync(
                _sayHelloOperation,
                _customEncoding,
                payload,
                invocation: invocation);

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
            ReadResult readResult;
            do
            {
                readResult = await reader.ReadAsync(cancel);
            }
            while (!readResult.IsCompleted);

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
                    throw new OperationNotFoundException();
                }

                Assert.That(request.PayloadEncoding, Is.EqualTo(_customEncoding));

                string greeting = _utf8.GetString((await ReadFullPayloadAsync(request.Payload, cancel)).Span);
                await request.Payload.CompleteAsync(); // done with payload

                Assert.That(greeting, Is.EqualTo(_greeting));

                // TODO: it makes no sense to have a simpler API for success.

                var payload = new ReadOnlyMemory<byte>[] { _utf8.GetBytes(_message) };
                var response = new OutgoingResponse(request, _resultType)
                {
                    Payload = payload,
                    PayloadEncoding = _customEncoding
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
