// Copyright (c) ZeroC, Inc.

namespace IceRpc.Builder.Internal;

/// <summary>Implements <see cref="IInvokerBuilder" /> for Microsoft's DI container.</summary>
internal sealed class InvokerBuilder : IInvokerBuilder
{
    public IServiceProvider ServiceProvider { get; }

    private bool _isIntoCalled;

    private readonly Pipeline _pipeline = new();

    public IInvokerBuilder Into(IInvoker invoker)
    {
        _ = _pipeline.Into(invoker);
        _isIntoCalled = true;
        return this;
    }

    public IInvokerBuilder Use(Func<IInvoker, IInvoker> interceptor)
    {
        _ = _pipeline.Use(interceptor);
        return this;
    }

    internal InvokerBuilder(IServiceProvider provider) => ServiceProvider = provider;

    internal IInvoker Build() =>
        _isIntoCalled ? _pipeline :
            throw new InvalidOperationException($"Missing call to {nameof(Into)} on the invoker builder.");
}
