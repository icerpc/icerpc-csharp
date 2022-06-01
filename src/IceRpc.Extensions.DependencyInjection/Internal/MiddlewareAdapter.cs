// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IceRpc.Extensions.DependencyInjection.Internal;

internal class MiddlewareAdapter<TMiddleware> : IDispatcher
{
    private readonly IDispatcher _dispatcher;
    private readonly TMiddleware _middleware;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
        _dispatcher.DispatchAsync(request, cancel);

    internal MiddlewareAdapter(TMiddleware middleware)
    {
        _middleware = middleware;
        if (_middleware is IDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }
        else
        {
            Type type = typeof(TMiddleware);

            try
            {
                MethodInfo method = type.GetMethod(
                    "DispatchAsync",
                    BindingFlags.Public | BindingFlags.Instance) ??
                    throw new InvalidOperationException(
                        $"{type.Name} does not have a public DispatchAsync instance method");

                if (method.ReturnType != typeof(ValueTask<OutgoingResponse>))
                {
                    throw new InvalidOperationException(
                        $"{type.Name}.DispatchAsync must return a {nameof(ValueTask<OutgoingResponse>)}");
                }

                ParameterInfo[] paramInfo = method.GetParameters();

                if (paramInfo[0].ParameterType != typeof(IncomingRequest))
                {
                    throw new InvalidOperationException(
                        $"the first parameter of {type.Name}.DispatchAsync must an {nameof(IncomingRequest)}");
                }
                if (paramInfo[^1].ParameterType != typeof(CancellationToken))
                {
                    throw new InvalidOperationException(
                        $"the last parameter of {type.Name}.DispatchAsync must a {nameof(CancellationToken)}");
                }
                if (paramInfo.Length < 3)
                {
                    throw new InvalidOperationException(
                        @$"{type.Name}.DispatchAsync must have at least 3 parameters when {type.Name
                        } is not an {nameof(IDispatcher)}");
                }

                _dispatcher = new InlineDispatcher((request, cancel) =>
                {
                    IServiceProviderFeature feature = request.Features.Get<IServiceProviderFeature>() ??
                        throw new InvalidOperationException("no service provider feature in request features");

                    object[] args = new object[paramInfo.Length];
                    args[0] = request;
                    args[^1] = cancel;
                    for (int i = 1; i < args.Length - 1; ++i)
                    {
                        args[i] = feature.ServiceProvider.GetRequiredService(paramInfo[i].ParameterType);
                    }
                    return (ValueTask<OutgoingResponse>)method.Invoke(_middleware, args)!;
                });
            }
            catch (AmbiguousMatchException exception)
            {
                throw new InvalidOperationException($"{type.Name} has multiple DispatchAsync overloads", exception);
            }
        }
    }
}
