// Copyright (c) ZeroC, Inc.

namespace IceRpc.Builder;

/// <summary>Provides the mechanism to configure an invoker when using Dependency Injection (DI).</summary>
public interface IInvokerBuilder
{
    /// <summary>Gets the service provider.</summary>
    /// <value>The <see cref="IServiceProvider" />.</value>
    IServiceProvider ServiceProvider { get; }

    /// <summary>Sets the last invoker of the invocation pipeline.</summary>
    /// <param name="lastInvoker">The last invoker.</param>
    /// <returns>This builder.</returns>
    IInvokerBuilder Into(IInvoker lastInvoker);

    /// <summary>Registers an interceptor.</summary>
    /// <param name="interceptor">The interceptor to register.</param>
    /// <returns>This builder.</returns>
    IInvokerBuilder Use(Func<IInvoker, IInvoker> interceptor);
}
