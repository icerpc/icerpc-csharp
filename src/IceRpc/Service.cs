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
    class Service : IService
    {
        internal delegate ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> Dispatcher(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel);

        private Dispatcher? _dispatcher;
        private ImmutableSortedSet<string> _ids = ImmutableSortedSet<string>.Empty;

        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel)
        {
            if (_ids == ImmutableSortedSet<string>.Empty)
            {
                ImmutableArray<string> ids = ImmutableArray<string>.Empty;
                foreach (Type t in GetType().GetInterfaces())
                {
                    ids = ids.AddRange(TypeExtensions.GetAllIceTypeIds(t));
                }
                _ids = ids.ToImmutableSortedSet(StringComparer.Ordinal);
            }
            return new(_ids);
        }

        public async ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel)
        {
            var array = (string[])await IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return Array.BinarySearch(array, typeId, StringComparer.Ordinal) >= 0;
        }

        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> DispatchAsync(
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

                // Create a case to dispatch each operation
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
                    Expression.Property(dispatchParam, typeof(Dispatch).GetProperty("Operation")!),
                    Expression.Throw(Expression.Constant(new OperationNotFoundException())),
                    cases);

                _dispatcher = Expression.Lambda<Dispatcher>(switchExpr, dispatchParam).Compile();
            }
            return _dispatcher(payload, dispatch, cancel);
        }
    }
}
