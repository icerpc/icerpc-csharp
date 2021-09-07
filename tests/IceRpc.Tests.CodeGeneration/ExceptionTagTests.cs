// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class ExceptionTagTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly ExceptionTagPrx _prx;

        public ExceptionTagTests()
        {
            _server = new Server
            {
                Dispatcher = new ExceptionTag(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };
            _prx = ExceptionTagPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public void ExceptionTag_Exceptions()
        {
            Assert.ThrowsAsync<TaggedException>(async () => await _prx.OpTaggedExceptionAsync(null, "foo", null));
        }
    }

    public class ExceptionTag : Service, IExceptionTag
    {
        public ValueTask OpDerivedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new DerivedException(false, p1, p2, p3, p2, p3);

        public ValueTask OpRequiredExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) =>
            throw new RequiredException(false, p1, p2, p3, p2 ?? "test", p3 ?? new AnotherStruct());

        public ValueTask OpTaggedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new TaggedException(false, p1, p2, p3);
    }
}
