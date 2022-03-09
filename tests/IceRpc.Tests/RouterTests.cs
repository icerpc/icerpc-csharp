// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class RouterTests
{
    /// <summary>Verifies that middleware cannot be added after a request has been dispatched.</summary>
    [Test]
    public async Task Cannot_add_middleware_after_a_request_has_been_dispatched()
    {
        // Arrange
        Router router = await CreateRouterAndCallDispatchAsync();

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => router.Use(next => next));
    }

    /// <summary>Verifies that a dispatcher cannot be mapped after a request has been dispatched.</summary>
    [Test]
    public async Task Cannot_map_a_dispatcher_after_a_request_has_been_dispatched()
    {
        // Arrange
        Router router = await CreateRouterAndCallDispatchAsync();

        // Act/Assert
        Assert.Throws<InvalidOperationException>(
            () => router.Map("/foo", new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)))));
    }

    /// <summary>Verifies that a dispatcher cannot be mounted after a request has been dispatched.</summary>
    [Test]
    public async Task Cannot_mount_a_dispatcher_after_a_request_has_been_dispatched()
    {
        // Arrange
        Router router = await CreateRouterAndCallDispatchAsync();

        // Act/Assert
        Assert.Throws<InvalidOperationException>(
            () => router.Mount("/foo", new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)))));
    }

    /// <summary>Verifies that creating a <see cref="Router"/> with an invalid prefix fails.</summary>
    [Test]
    public void Creating_a_router_with_invalid_prefix_fails() =>
        Assert.Throws<FormatException>(() => new Router("foo"));

    /// <summary>Verifies that exact matches are selected before than prefix matches.</summary>
    /// <param name="path">The invocation path.</param>
    [TestCase("/foo")]
    [TestCase("/foo/a/b/c/d")]
    [TestCase("/")]
    [TestCase("///")] // bad form but nevertheless still works
    [TestCase("/foo/////")] // bad form but nevertheless still works
    public async Task Exact_match_is_selected_first(string path)
    {
        // Arrange
        var router = new Router();
        string? currentPath = null;

        router.Map(path, new InlineDispatcher(
            (current, cancel) =>
            {
                currentPath = current.Path;
                return new(new OutgoingResponse(current));
            }));

        router.Mount(path, new InlineDispatcher((current, cancel) => new(new OutgoingResponse(current))));

        // Act
        _ = await router.DispatchAsync(
            new IncomingRequest(Protocol.IceRpc)
            {
                Path = path
            });

        // Assert
        Assert.That(currentPath, Is.EqualTo(path));
    }

    /// <summary>Verifies that <see cref="Router.Map(string, IDispatcher)"/> fails when using an invalid path.</summary>
    [Test]
    public void Maping_an_invalid_path_fails()
    {
        var router = new Router();

        Assert.Throws<FormatException>(() => router.Mount("foo", ConnectionOptions.DefaultDispatcher));
    }

    /// <summary>Verifies that a dispatcher mounted using <see cref="Router.Mount(string, IDispatcher)"/>
    /// is selected for dispatch requests with a path starting with the given prefix.</summary>
    /// <param name="prefix">The prefix to mount the dispatcher.</param>
    /// <param name="path">The path for the request.</param>
    /// <returns></returns>
    [TestCase("/foo", "/foo/bar")]
    [TestCase("/foo/", "/foo/bar")]
    [TestCase("/foo/bar///", "/foo/bar")] // ignores trailing slash(es) in prefix
    [TestCase("/foo///bar/a", "/foo///bar/a/b/c/d")]
    public async Task Mounted_dispatcher_is_used_for_paths_starting_with_prefix(string prefix, string path)
    {
        // Arrange
        var router = new Router();
        string? currentPath = null;

        router.Mount(prefix, new InlineDispatcher(
            (current, cancel) =>
            {
                currentPath = current.Path;
                return new(new OutgoingResponse(current));
            }));

        // Act
        _ = await router.DispatchAsync(
            new IncomingRequest(Protocol.IceRpc)
            {
                Path = path
            });

        // Assert
        Assert.That(currentPath, Is.EqualTo(path));
    }

    /// <summary>Verifies that <see cref="Router.Mount(string, IDispatcher)"/> fails when using an invalid path.
    /// </summary>
    [Test]
    public void Mounting_an_invalid_path_fails()
    {
        var router = new Router();

        Assert.Throws<FormatException>(() => router.Mount("foo", ConnectionOptions.DefaultDispatcher));
    }

    /// <summary>Verifies that a path that doesn't match any of the registered routes throws a dispatch
    /// exception with <see cref="DispatchErrorCode.ServiceNotFound"/> error code.</summary>
    [Test]
    public void Path_not_found()
    {
        var router = new Router();

        DispatchException ex = Assert.ThrowsAsync<DispatchException>(
            async () => await router.DispatchAsync(new IncomingRequest(Protocol.IceRpc)));

        Assert.That(ex.ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
    }

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

    /// <summary>Verifies that the middleware are called in the expected order before dispatching the request to the
    /// inner most router.</summary>
    /// <param name="prefix">The prefix for the sub-router.</param>
    /// <param name="subprefix">The prefix for the sub-sub-router</param>
    /// <param name="path">The path for the request.</param>
    /// <param name="subpath">The path for the dispatcher in the inner most router.</param>
    /// <returns></returns>
    [TestCase("/foo", "/bar", "/foo/bar/abc", "/abc")]
    [TestCase("/foo/", "/bar/", "/foo/bar/abc", "/abc")]
    public async Task Router_with_middleware_and_nested_subrouters(
        string prefix,
        string subprefix,
        string path,
        string subpath)
    {
        // Arrange
        var router = new Router();

        var calls = new List<string>();
        var expectedCalls = new List<string>() { "middleware-0", "middleware-1", "middleware-2", "dispatcher" };

        router.Use(next => new InlineDispatcher(
            (request, cancel) =>
            {
                calls.Add("middleware-0");
                return next.DispatchAsync(request, cancel);
            }));

        router.Route(prefix, r =>
        {
            r.Use(next => new InlineDispatcher(
                (request, cancel) =>
                {
                    calls.Add("middleware-1");
                    return next.DispatchAsync(request, cancel);
                }));

            r.Route(subprefix, r =>
            {
                r.Use(next => new InlineDispatcher(
                   (request, cancel) =>
                   {
                       calls.Add("middleware-2");
                       return next.DispatchAsync(request, cancel);
                   }));

                r.Map(subpath, new InlineDispatcher(
                    (request, cancel) =>
                    {
                        calls.Add("dispatcher");
                        return new(new OutgoingResponse(request));
                    }));
            });
        });

        // Act
        _ = await router.DispatchAsync(
            new IncomingRequest(Protocol.IceRpc)
            {
                Path = path
            });

        // Assert
        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    /// <summary>Helper method that creates a router and calls
    /// <see cref="IDispatcher.DispatchAsync(IncomingRequest, CancellationToken)"/> this ensures that the router
    /// internal dispatcher is initialized.</summary>
    /// <returns>The router.</returns>
    private static async Task<Router> CreateRouterAndCallDispatchAsync()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var router = new Router();
        router.Mount("/", dispatcher);

        _ = await router.DispatchAsync(new IncomingRequest(Protocol.IceRpc));
        return router;
    }
}
