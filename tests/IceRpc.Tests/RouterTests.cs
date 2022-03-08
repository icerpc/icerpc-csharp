// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class RouterTests
{
    /// <summary>Verifies that <see cref="Router.Mount(string, IDispatcher)"/> fails when using an invalid path.
    /// </summary>
    [Test]
    public void Mounting_an_invalid_path_fails()
    {
        var router = new Router();

        Assert.Throws<FormatException>(() => router.Mount("foo", ConnectionOptions.DefaultDispatcher));
    }

    /// <summary>Verifies that <see cref="Router.Map(string, IDispatcher)"/> fails when using an invalid path.</summary>
    [Test]
    public void Maping_an_invalid_path_fails()
    {
        var router = new Router();

        Assert.Throws<FormatException>(() => router.Mount("foo", ConnectionOptions.DefaultDispatcher));
    }

    /// <summary>Verifies that creating a <see cref="Router"/> with an invalid prefix fails.</summary>
    [Test]
    public void Creating_a_router_with_invalid_prefix_fails() =>
        Assert.Throws<FormatException>(() => new Router("foo"));

    [TestCase("/foo/", "/foo")]
    [TestCase("/foo//", "/foo")]
    [TestCase("//foo//", "//foo")]
    [TestCase("//foo//bar//baz", "//foo//bar//baz")]
    [TestCase("//foo//bar//baz//", "//foo//bar//baz")]
    [TestCase("/", "")]
    [TestCase("////", "")]
    public void Router_normalizes_the_prefix(string prefix, string normalizedPrefix)
    {
        var router = new Router(prefix);

        Assert.That(router.AbsolutePrefix, Is.EqualTo(normalizedPrefix));
    }

    /// <summary>Verifies that middleware cannot be added after a request has been dispatched.</summary>
    [Test]
    public async Task Cannot_add_middleware_after_a_request_has_been_dispatched()
    {
        // Arrange
        var router = new Router();
        router.Mount("/", new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))));

        _ = await ((IDispatcher)router).DispatchAsync(
            new IncomingRequest(
                Protocol.IceRpc,
                "/",
                "",
                "",
                PipeReader.Create(Stream.Null),
                Encoding.Slice20,
                PipeWriter.Create(Stream.Null)));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => router.Use(next => next));
    }
}
