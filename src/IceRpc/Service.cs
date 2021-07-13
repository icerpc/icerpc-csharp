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
        private delegate ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> Dispatcher(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel);

        private Dispatcher? _dispatcher;
        private readonly Lazy<ImmutableSortedSet<string>> _ids;

        /// <summary>Constructs a new service.</summary>
        public Service()
        {
            _ids = new Lazy<ImmutableSortedSet<string>>(
                () =>
                {
                    ImmutableArray<string> ids = ImmutableArray<string>.Empty;
                    foreach (Type t in GetType().GetInterfaces())
                    {
                        ids = ids.AddRange(TypeExtensions.GetAllIceTypeIds(t));
                    }
                    return ids.ToImmutableSortedSet(StringComparer.Ordinal);
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
            if (_dispatcher == null)
            {
                var operations = new List<(MethodInfo, string)>();
                foreach (Type t in GetType().GetInterfaces())
                {
                    MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
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

                ParameterExpression payloadParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>));
                ParameterExpression dispatchParam = Expression.Parameter(typeof(Dispatch));
                ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));

                // Create a switch case to dispatch each operation
                var cases = new SwitchCase[operations.Count];
                for (int i = 0; i < cases.Length; ++i)
                {
                    (MethodInfo method, string operationName) = operations[i];
                    cases[i] = Expression.SwitchCase(
                        Expression.Call(Expression.Constant(this),
                                        method,
                                        payloadParam,
                                        dispatchParam,
                                        cancelParam),
                        Expression.Constant(operationName));
                }

                // Create a switch expression that switchs on dispatch.Operation
                SwitchExpression switchExpr = Expression.Switch(
                    typeof(ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)>),
                    Expression.Property(dispatchParam, typeof(Dispatch).GetProperty("Operation")!),
                    Expression.Throw(
                        Expression.Constant(new OperationNotFoundException()),
                        typeof(ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)>)),
                    null,
                    cases);

                _dispatcher = Expression.Lambda<Dispatcher>(
                    switchExpr,
                    payloadParam,
                    dispatchParam,
                    cancelParam).Compile();
            }
            return _dispatcher(payload, dispatch, cancel);
        }
    }
}
