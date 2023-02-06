// Copyright (c) ZeroC, Inc.

namespace IceRpc.Builder.Internal;

/// <summary>Provides the default implementation of <see cref="IInvokerBuilder" />.</summary>
internal class InvokerBuilder : IInvokerBuilder
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    private bool _isIntoCalled;

    private readonly Pipeline _pipeline = new();

    /// <inheritdoc/>
    public IInvokerBuilder Into(IInvoker invoker)
    {
        _pipeline.Into(invoker);
        _isIntoCalled = true;
        return this;
    }

    /// <inheritdoc/>
    public IInvokerBuilder Use(Func<IInvoker, IInvoker> interceptor)
    {
        _pipeline.Use(interceptor);
        return this;
    }

    internal InvokerBuilder(IServiceProvider provider) => ServiceProvider = provider;

    internal IInvoker Build()
    {
        if (!_isIntoCalled)
        {
            throw new InvalidOperationException($"{nameof(Into)} not called on invoker builder");
        }
        return _pipeline;
    }
}
