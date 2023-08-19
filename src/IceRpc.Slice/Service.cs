// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Ice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Represents the base class of all services that implement Slice-defined interfaces.</summary>
public class Service : IDispatcher, IIceObjectService
{
    /// <summary>A delegate that matches the signature of the generated SliceDXxx methods. For the generated methods,
    /// the type of <c>target</c> is the type of the generated service interface, whereas for this delegate it's
    /// <see cref="object" />.</summary>
    private delegate ValueTask<OutgoingResponse> DispatchMethod(
        object target,
        IncomingRequest request,
        CancellationToken cancellationToken);

    // A per-type cache of dispatch methods
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, DispatchMethod>> _cache = new();

    // A dictionary of operation name to DispatchMethod used by DispatchAsync implementation.
    private readonly IReadOnlyDictionary<string, DispatchMethod> _dispatchMethods;

    /// <summary>Constructs a new service.</summary>
    public Service()
    {
        _dispatchMethods = _cache.GetOrAdd(GetType(), type =>
            {
                ParameterExpression targetParam = Expression.Parameter(typeof(object));
                ParameterExpression requestParam = Expression.Parameter(typeof(IncomingRequest));
                ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));

                var methods = new Dictionary<string, DispatchMethod>();

                foreach (Type interfaceType in type.GetInterfaces())
                {
                    foreach (MethodInfo method in interfaceType.GetMethods(
                        BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        object[] attributes = method.GetCustomAttributes(typeof(SliceOperationAttribute), false);
                        if (attributes.Length > 0 && attributes[0] is SliceOperationAttribute attribute)
                        {
                            if (!methods.TryAdd(
                                attribute.Value,
                                Expression.Lambda<DispatchMethod>(
                                    Expression.Call(
                                        method,
                                        Expression.Convert(targetParam, type),
                                        requestParam,
                                        cancelParam),
                                    targetParam,
                                    requestParam,
                                    cancelParam).Compile()))
                            {
                                throw new InvalidOperationException(
                                    $"Duplicate operation name {attribute.Value}: {type.FullName} cannot implement multiple Slice operations with the same name.");
                            }
                        }
                    }
                }
                return methods;
            });
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (_dispatchMethods.TryGetValue(request.Operation, out DispatchMethod? dispatchMethod))
        {
            try
            {
                return await dispatchMethod(this, request, cancellationToken).ConfigureAwait(false);
            }
            catch (DispatchException exception)
            {
                if (exception.ConvertToInternalError)
                {
                    return new OutgoingResponse(request, StatusCode.InternalError, message: null, exception);
                }
                return new OutgoingResponse(request, exception.StatusCode);
            }
        }
        else
        {
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
