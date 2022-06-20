// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Bidir;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the bidir middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class BidirDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="BidirMiddleware"/> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseBidir(this IDispatcherBuilder builder) =>
        builder.Use(next => new BidirMiddleware(next));
}
