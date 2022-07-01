// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Builder.Internal;

/// <summary>Provides the default implementation of <see cref="IInvokerBuilder"/>.</summary>
internal class InvokerBuilder : IInvokerBuilder
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    private readonly Pipeline _pipeline = new();

    /// <inheritdoc/>
    public IInvokerBuilder Into(IInvoker invoker)
    {
        _pipeline.Into(invoker);
        return this;
    }

    /// <inheritdoc/>
    public IInvokerBuilder Use(Func<IInvoker, IInvoker> interceptor)
    {
        _pipeline.Use(interceptor);
        return this;
    }

    internal InvokerBuilder(IServiceProvider provider) => ServiceProvider = provider;

    internal IInvoker Build() => _pipeline;
}
