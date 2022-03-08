// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(30000)]
public class InvocationTests
{
    /// <summary>Verifies that the request context feature is set to the invocation context.
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task Invocation_context()
    {
        // Arrange
        var invocation = new Invocation
        {
            Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "bar" })
        };

        IDictionary<string, string>? context = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            context = request.Features.GetContext();
            return Task.FromResult(new IncomingResponse(request, ResultType.Success, PipeReader.Create(Stream.Null)));
        });

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = "icerpc://localhost"
        });
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Path = "/",
            Invoker = invoker,
        };

        // Act
        await proxy.InvokeAsync(
            "",
            Encoding.Slice20,
            PipeReader.Create(Stream.Null),
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            invocation);

        // Assert
        Assert.That(context, Is.EquivalentTo(invocation.Features.GetContext()));
    }
}
