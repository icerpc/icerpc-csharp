// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Base class of all service implementations.</summary>
    public class Service : IService, IDispatcher
    {
        /// <summary>A delegate that matches the signature of the generated IceDXxx methods, the only difference is that
        /// for the generated methods <para>target</para> type is the type of the generated service interface.</summary>
        private delegate ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDMethod(
            object target,
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel);

        private ImmutableSortedDictionary<string, IceDMethod> _dispatchMethods =
            ImmutableSortedDictionary<string, IceDMethod>.Empty;
        private static ImmutableDictionary<Type, ImmutableSortedDictionary<string, IceDMethod>> _dispatchMethodsCache =
            ImmutableDictionary<Type, ImmutableSortedDictionary<string, IceDMethod>>.Empty;
        // proctects the dispatch methods and type ids caches
        private static readonly object _mutex = new();
        private readonly Lazy<ImmutableSortedSet<string>> _ids;
        private static ImmutableDictionary<Type, ImmutableSortedSet<string>> _typeIdsCache =
            ImmutableDictionary<Type, ImmutableSortedSet<string>>.Empty;

        /// <summary>Constructs a new service.</summary>
        public Service()
        {
            // Cache per type
            _ids = new Lazy<ImmutableSortedSet<string>>(
                () =>
                {
                    Type type = GetType();
                    if (!_typeIdsCache.TryGetValue(type, out ImmutableSortedSet<string>? ids))
                    {
                        ImmutableArray<string> newIds = ImmutableArray<string>.Empty;
                        foreach (Type interfaceType in type.GetInterfaces())
                        {
                            newIds = newIds.AddRange(TypeExtensions.GetAllIceTypeIds(interfaceType));
                        }
                        ids = newIds.ToImmutableSortedSet(StringComparer.Ordinal);

                        lock (_mutex)
                        {
                            if (!_typeIdsCache.ContainsKey(type))
                            {
                                _typeIdsCache = _typeIdsCache.Add(type, ids);
                            }
                        }
                    }
                    return ids;
                });
        }

        /// <inheritdoc/>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) => new(_ids.Value);

        /// <inheritdoc/>
        public ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel) =>
            new(_ids.Value.Contains(typeId));

        /// <inheritdoc/>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <inheritdoc/>
        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            var dispatch = new Dispatch(request);
            try
            {
                ReadOnlyMemory<byte> requestPayload = await request.GetPayloadAsync(cancel).ConfigureAwait(false);
                (ReadOnlyMemory<ReadOnlyMemory<byte>> responsePayload, RpcStreamWriter? streamWriter) =
                    await DispatchAsync(requestPayload, dispatch, cancel).ConfigureAwait(false);

                return new OutgoingResponse(dispatch, responsePayload, streamWriter);
            }
            catch (RemoteException exception)
            {
                exception.Features = dispatch.ResponseFeatures;
                throw;
            }
        }

        /// <summary>Dispatches an incoming request and returns the corresponding response.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch properties, which include properties of both the request and response.
        /// </param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response payload and optional stream writer.</returns>
        private ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> DispatchAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            if (_dispatchMethods.IsEmpty)
            {
                Type type = GetType();
                if (!_dispatchMethodsCache.TryGetValue(type,
                    out ImmutableSortedDictionary<string, IceDMethod>? dispatchMethods))
                {
                    var operations = new List<(MethodInfo, string)>();
                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        MethodInfo[] methods = interfaceType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
                        foreach (MethodInfo method in methods)
                        {
                            object[] attributes = method.GetCustomAttributes(typeof(OperationAttribute), false);
                            if (attributes.Length > 0 && attributes[0] is OperationAttribute attribute)
                            {
                                operations.Add((method, attribute.Value));
                            }
                        }
                    }

                    // There is atleast the 3 builtin operations
                    Debug.Assert(operations.Count >= 3);

                    ParameterExpression targetParam = Expression.Parameter(typeof(object));
                    ParameterExpression payloadParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>));
                    ParameterExpression dispatchParam = Expression.Parameter(typeof(Dispatch));
                    ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));

                    // Create a switch case to dispatch each operation
                    var cases = new SwitchCase[operations.Count];
                    var newDispatchMethods = new Dictionary<string, IceDMethod>();
                    for (int i = 0; i < cases.Length; ++i)
                    {
                        (MethodInfo method, string operationName) = operations[i];
                        newDispatchMethods[operationName] = Expression.Lambda<IceDMethod>(
                            Expression.Call(
                                method,
                                Expression.Convert(targetParam, type),
                                payloadParam,
                                dispatchParam,
                                cancelParam),
                            targetParam,
                            payloadParam,
                            dispatchParam,
                            cancelParam).Compile();
                    };

                    lock (_mutex)
                    {
                        dispatchMethods = newDispatchMethods.ToImmutableSortedDictionary();
                        _dispatchMethods = dispatchMethods;
                        if (!_dispatchMethodsCache.ContainsKey(type))
                        {
                            _dispatchMethodsCache = _dispatchMethodsCache.Add(type, dispatchMethods);
                        }
                    }
                }
                else
                {
                    _dispatchMethods = dispatchMethods;
                }
            }

            if (_dispatchMethods.TryGetValue(dispatch.Operation, out IceDMethod? dispatchMethod))
            {
                return dispatchMethod(this, payload, dispatch, cancel);
            }
            else
            {
                throw new OperationNotFoundException();
            }
        }
    }
}
