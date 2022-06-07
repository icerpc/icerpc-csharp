// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Builder;

/// <summary>Provides the mechanism to configure an invoker when using Dependency Injection (DI).</summary>
public interface IInvokerBuilder
{
    /// <summary>Gets the service provider.</summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>Registers an interceptor.</summary>
    /// <param name="interceptor">The interceptor to register.</param>
    /// <returns>This builder.</returns>
    IInvokerBuilder Use(Func<IInvoker, IInvoker> interceptor);
}
