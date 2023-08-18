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
    /// <summary>A delegate that matches the signature of the generated SliceDXxx methods. For the generated
    /// methods, the type of <c>target</c> is the type of the generated service interface, whereas for this
    /// delegate it's <see cref="object" />.</summary>
    private delegate ValueTask<OutgoingResponse> DispatchMethod(
        object target,
        IncomingRequest request,
        CancellationToken cancellationToken);

    // A per type cache of dispatch methods and type IDs.
    private static readonly ConcurrentDictionary<Type, (IReadOnlyDictionary<string, DispatchMethod> Methods, IReadOnlySet<string> TypeIds)> _cache =
       new();

    // A dictionary of operation name to DispatchMethod used by DispatchAsync implementation.
    private readonly IReadOnlyDictionary<string, DispatchMethod> _dispatchMethods;

    // The service type IDs.
    private readonly IReadOnlySet<string> _typeIds;

    /// <summary>Constructs a new service.</summary>
    public Service()
    {
        (_dispatchMethods, _typeIds) = _cache.GetOrAdd(GetType(), type =>
            {
                ParameterExpression targetParam = Expression.Parameter(typeof(object));
                ParameterExpression requestParam = Expression.Parameter(typeof(IncomingRequest));
                ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));

                var methods = new Dictionary<string, DispatchMethod>();
                var typeIds = new SortedSet<string>();

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

                // There is at least the 3 ice_ operations
                Debug.Assert(methods.Count >= 3);

                foreach (Type interfaceType in type.GetInterfaces())
                {
                    typeIds.UnionWith(interfaceType.GetAllSliceTypeIds());
                }

                return (methods, typeIds);
            });
    }

    /// <inheritdoc/>
    public virtual ValueTask<IEnumerable<string>> IceIdsAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken) =>
        new(_typeIds);

    /// <inheritdoc/>
    public virtual ValueTask<bool> IceIsAAsync(
        string id,
        IFeatureCollection features,
        CancellationToken cancellationToken) =>
        new(_typeIds.Contains(id));

    /// <inheritdoc/>
    public virtual ValueTask IcePingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (_dispatchMethods.TryGetValue(request.Operation, out DispatchMethod? dispatchMethod))
        {
            return await dispatchMethod(this, request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
