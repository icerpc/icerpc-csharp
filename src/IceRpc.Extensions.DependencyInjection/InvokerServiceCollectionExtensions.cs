// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IServiceCollection" /> to add an invoker.</summary>
public static class InvokerServiceCollectionExtensions
{
    /// <summary>Extension methods for <see cref="IServiceCollection" />.</summary>
    /// <param name="services">The service collection to add services to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Adds an <see cref="IInvoker" /> to this service collection.</summary>
        /// <param name="configure">The action to configure the new invoker.</param>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddIceRpcInvoker(Action<IInvokerBuilder> configure) =>
            services.AddSingleton(provider =>
            {
                var builder = new InvokerBuilder(provider);
                configure(builder);
                return builder.Build();
            });
    }
}
