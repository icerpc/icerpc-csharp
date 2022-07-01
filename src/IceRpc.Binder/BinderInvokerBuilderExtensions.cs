// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Binder;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the binder interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class BinderInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="BinderInterceptor"/> to this builder. This interceptor relies on the
    /// <see cref="IClientConnectionProvider"/> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseBinder(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(IClientConnectionProvider)) is IClientConnectionProvider connectionProvider ?
        builder.Use(next => new BinderInterceptor(next, connectionProvider)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(IClientConnectionProvider)} in service container");
}
