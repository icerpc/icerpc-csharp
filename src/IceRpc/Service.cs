// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc
{
    /// <summary>Base class of all services that implement Slice-defined interfaces.</summary>
    /// <remarks>This class is part of the Slice engine code.</remarks>
    public class Service : IService, IDispatcher
    {
        /// <summary>A delegate that matches the signature of the generated SliceDXxx methods. For the generated
        /// methods, the type of <para>target</para> is the type of the generated service interface, whereas for this
        /// delegate it's <see cref="object"/>.</summary>
        private delegate ValueTask<(SliceEncoding, PipeReader, PipeReader?)> DispatchMethod(
            object target,
            IncomingRequest request,
            CancellationToken cancel);

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
                            object[] attributes = method.GetCustomAttributes(typeof(OperationAttribute), false);
                            if (attributes.Length > 0 && attributes[0] is OperationAttribute attribute)
                            {
                                methods.Add(
                                    attribute.Value,
                                    Expression.Lambda<DispatchMethod>(
                                        Expression.Call(
                                            method,
                                            Expression.Convert(targetParam, type),
                                            requestParam,
                                            cancelParam),
                                        targetParam,
                                        requestParam,
                                        cancelParam).Compile());
                            }
                        }
                    }

                    // There is at least the 3 built-in operations
                    Debug.Assert(methods.Count >= 3);

                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        typeIds.UnionWith(interfaceType.GetAllSliceTypeIds());
                    }

                    return (methods, typeIds);
                });
        }

        /// <inheritdoc/>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds);

        /// <inheritdoc/>
        public ValueTask<bool> IceIsAAsync(string id, Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds.Contains(id));

        /// <inheritdoc/>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <inheritdoc/>
        public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (_dispatchMethods.TryGetValue(request.Operation, out DispatchMethod? dispatchMethod))
            {
                (SliceEncoding payloadEncoding, PipeReader responsePayloadSource, PipeReader? responsePayloadSourceStream) =
                    await dispatchMethod(this, request, cancel).ConfigureAwait(false);

                return new OutgoingResponse(request)
                {
                    PayloadSource = responsePayloadSource,
                    PayloadSourceStream = responsePayloadSourceStream,
                    PayloadEncoding = payloadEncoding,
                };
            }
            else
            {
                throw new OperationNotFoundException();
            }
        }
    }
}
