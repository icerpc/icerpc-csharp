// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class RouterTests
{
    private static readonly IDispatcher _defaultDispatcher = new InlineDispatcher(
        (request, cancel) => throw new DispatchException(
            DispatchErrorCode.ServiceNotFound,
            RetryPolicy.OtherReplica));


    /// <summary>Ensure that <see cref="Router.Mount(string, IDispatcher)"/> fails when using an invalid path.</summary>
    /// <param name="path"></param>
    [Test]
    public void Mount_an_invalid_path_fails()
    {
        Router router = new();

        Assert.Throws<FormatException>(() => router.Mount("foo", _defaultDispatcher));
    }

    /// <summary>Ensure that <see cref="Router.Map(string, IDispatcher)"/> fails when using an invalid path.</summary>
    [Test]
    public void Map_an_invalid_path_fails()
    {
        Router router = new();

        Assert.Throws<FormatException>(() => router.Mount("foo", _defaultDispatcher));
    }

    /// <summary>Ensure that creating a <see cref="Router"/> with an invalid prefix fails.</summary>
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
}
