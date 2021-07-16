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

        // A per type cache of dispatch methods and type IDs.
        private static ImmutableDictionary<Type, (ImmutableDictionary<string, IceDMethod> Methods, ImmutableSortedSet<string> TypeIds)> _cache =
           ImmutableDictionary<Type, (ImmutableDictionary<string, IceDMethod> Methods, ImmutableSortedSet<string> TypeIds)>.Empty;

        // A dictionary of operation name to IceDMethod used by DispatchAsync implementation.
        private readonly ImmutableDictionary<string, IceDMethod> _dispatchMethods;
        // The service type IDs.
        private readonly ImmutableSortedSet<string> _typeIds;

        /// <summary>Constructs a new service.</summary>
        public Service()
        {
            // Initialize IceDMethods cache
            Type type = GetType();
            if (!_cache.TryGetValue(
                type,
                out (ImmutableDictionary<string, IceDMethod> Methods, ImmutableSortedSet<string> TypeIds) entry))
            {
                ParameterExpression targetParam = Expression.Parameter(typeof(object));
                ParameterExpression payloadParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>));
                ParameterExpression dispatchParam = Expression.Parameter(typeof(Dispatch));
                ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));
                ImmutableDictionary<string, IceDMethod>.Builder dispatchMethodsBuilder =
                    ImmutableDictionary.CreateBuilder<string, IceDMethod>();
                foreach (Type interfaceType in type.GetInterfaces())
                {
                    MethodInfo[] methods = interfaceType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
                    foreach (MethodInfo method in methods)
                    {
                        object[] attributes = method.GetCustomAttributes(typeof(OperationAttribute), false);
                        if (attributes.Length > 0 && attributes[0] is OperationAttribute attribute)
                        {
                            dispatchMethodsBuilder.Add(
                                attribute.Value,
                                Expression.Lambda<IceDMethod>(
                                    Expression.Call(
                                        method,
                                        Expression.Convert(targetParam, type),
                                        payloadParam,
                                        dispatchParam,
                                        cancelParam),
                                    targetParam,
                                    payloadParam,
                                    dispatchParam,
                                    cancelParam).Compile());
                        }
                    }
                }

                // There is at least the 3 built-in operations
                entry.Methods = dispatchMethodsBuilder.ToImmutableDictionary();
                Debug.Assert(entry.Methods.Count >= 3);

                var newIdsBuilder = ImmutableSortedSet.CreateBuilder<string>();
                foreach (Type interfaceType in type.GetInterfaces())
                {
                    newIdsBuilder.UnionWith(TypeExtensions.GetAllIceTypeIds(interfaceType));
                }
                entry.TypeIds = newIdsBuilder.ToImmutableSortedSet(StringComparer.Ordinal);

                _cache = _cache.SetItem(type, entry);
            }
            _dispatchMethods = entry.Methods;
            _typeIds =  entry.TypeIds;
        }

        /// <inheritdoc/>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds);

        /// <inheritdoc/>
        public ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel) =>
            new(_typeIds.Contains(typeId));

        /// <inheritdoc/>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <inheritdoc/>
        public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            var dispatch = new Dispatch(request);
            try
            {
                ReadOnlyMemory<byte> requestPayload = await request.GetPayloadAsync(cancel).ConfigureAwait(false);
                if (_dispatchMethods.TryGetValue(dispatch.Operation, out IceDMethod? dispatchMethod))
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
