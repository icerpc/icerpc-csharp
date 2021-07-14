﻿// Copyright (c) ZeroC, Inc. All rights reserved.

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

        // A dictionary of operation name to IceDMethod ussed by DispatchAsync implementation, lazzy initialized.
        private readonly Lazy<ImmutableSortedDictionary<string, IceDMethod>> _dispatchMethods;
        // A per type cache of dispatch methods dictionary.
        private static ImmutableDictionary<Type, ImmutableSortedDictionary<string, IceDMethod>> _dispatchMethodsCache =
            ImmutableDictionary<Type, ImmutableSortedDictionary<string, IceDMethod>>.Empty;
        // proctects the dispatch methods and type ids caches.
        private static readonly object _mutex = new();
        // The service type IDs.
        private readonly Lazy<ImmutableSortedSet<string>> _typeIds;
        // A per type cache of type IDs.
        private static ImmutableDictionary<Type, ImmutableSortedSet<string>> _typeIdsCache =
            ImmutableDictionary<Type, ImmutableSortedSet<string>>.Empty;

        /// <summary>Constructs a new service.</summary>
        public Service()
        {
            // IceDMethods cache
            _dispatchMethods = new Lazy<ImmutableSortedDictionary<string, IceDMethod>>(
                () =>
                {
                    Type type = GetType();
                    if (!_dispatchMethodsCache.TryGetValue(type,
                        out ImmutableSortedDictionary<string, IceDMethod>? dispatchMethods))
                    {
                        ParameterExpression targetParam = Expression.Parameter(typeof(object));
                        ParameterExpression payloadParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>));
                        ParameterExpression dispatchParam = Expression.Parameter(typeof(Dispatch));
                        ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));
                        var newDispatchMethods = new Dictionary<string, IceDMethod>();
                        foreach (Type interfaceType in type.GetInterfaces())
                        {
                            MethodInfo[] methods = interfaceType.GetMethods(BindingFlags.Static |
                                                                            BindingFlags.NonPublic);
                            foreach (MethodInfo method in methods)
                            {
                                object[] attributes = method.GetCustomAttributes(typeof(OperationAttribute), false);
                                if (attributes.Length > 0 && attributes[0] is OperationAttribute attribute)
                                {
                                    newDispatchMethods[attribute.Value] = Expression.Lambda<IceDMethod>(
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
                                }
                            }
                        }

                        // There is atleast the 3 builtin operations
                        Debug.Assert(newDispatchMethods.Count >= 3);
                        dispatchMethods = newDispatchMethods.ToImmutableSortedDictionary();
                        lock (_mutex)
                        {
                            if (!_dispatchMethodsCache.ContainsKey(type))
                            {
                                _dispatchMethodsCache = _dispatchMethodsCache.Add(type, dispatchMethods);
                            }
                        }
                    }
                    return dispatchMethods;
                });

            // Type ids cache
            _typeIds = new Lazy<ImmutableSortedSet<string>>(
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
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds.Value);

        /// <inheritdoc/>
        public ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds.Value.Contains(typeId));

        /// <inheritdoc/>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <inheritdoc/>
        public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            var dispatch = new Dispatch(request);
            try
            {
                ReadOnlyMemory<byte> requestPayload = await request.GetPayloadAsync(cancel).ConfigureAwait(false);
                if (_dispatchMethods.Value.TryGetValue(dispatch.Operation, out IceDMethod? dispatchMethod))
                {
                    (ReadOnlyMemory<ReadOnlyMemory<byte>> responsePayload, RpcStreamWriter? streamWriter) =
                        await dispatchMethod(this, requestPayload, dispatch, cancel).ConfigureAwait(false);
                    return new OutgoingResponse(dispatch, responsePayload, streamWriter);
                }
                else
                {
                    throw new OperationNotFoundException();
                }
            }
            catch (RemoteException exception)
            {
                exception.Features = dispatch.ResponseFeatures;
                throw;
            }
        }
    }
}
