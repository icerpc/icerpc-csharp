// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Bidir;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the bidir interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class BidirInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="BidirInterceptor"/> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseBidir(this IInvokerBuilder builder) =>
        builder.Use(next => new BidirInterceptor(next));
}
