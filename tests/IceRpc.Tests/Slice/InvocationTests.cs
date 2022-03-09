// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

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
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Path = "/",
            Invoker = new InlineInvoker((request, cancel) =>
            {
                context = request.Features.GetContext();
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await proxy.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            invocation);

        // Assert
        Assert.That(context, Is.EqualTo(invocation.Features.GetContext()));
    }
}
