// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc
{
    /// <summary>Base class of all service implementations.</summary>
    public class Service : IService, IDispatcher
    {
        /// <summary>A delegate that matches the signature of the generated IceDXxx methods, the only difference is that
        /// for the generated methods <para>target</para> type is the type of the generated service interface.</summary>
        private delegate ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, IStreamParamSender?)> IceDMethod(
            object target,
            IncomingRequest request,
            Dispatch dispatch,
            CancellationToken cancel);

        // A per type cache of dispatch methods and type IDs.
        private static readonly ConcurrentDictionary<Type, (IReadOnlyDictionary<string, IceDMethod> Methods, IReadOnlySet<string> TypeIds)> _cache =
           new();

        // A dictionary of operation name to IceDMethod used by DispatchAsync implementation.
        private readonly IReadOnlyDictionary<string, IceDMethod> _dispatchMethods;
        // The service type IDs.
        private readonly IReadOnlySet<string> _typeIds;

        /// <summary>Constructs a new service.</summary>
        public Service()
        {
            (_dispatchMethods, _typeIds) = _cache.GetOrAdd(GetType(), type =>
                {
                    ParameterExpression targetParam = Expression.Parameter(typeof(object));
                    ParameterExpression requestParam = Expression.Parameter(typeof(IncomingRequest));
                    ParameterExpression dispatchParam = Expression.Parameter(typeof(Dispatch));
                    ParameterExpression cancelParam = Expression.Parameter(typeof(CancellationToken));

                    var methods = new Dictionary<string, IceDMethod>();
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
                                    Expression.Lambda<IceDMethod>(
                                        Expression.Call(
                                            method,
                                            Expression.Convert(targetParam, type),
                                            requestParam,
                                            dispatchParam,
                                            cancelParam),
                                        targetParam,
                                        requestParam,
                                        dispatchParam,
                                        cancelParam).Compile());
                            }
                        }
                    }

                    // There is at least the 3 built-in operations
                    Debug.Assert(methods.Count >= 3);

                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        typeIds.UnionWith(interfaceType.GetAllIceTypeIds());
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
            // We need dispatch here to extract its features and set them on remote exception below.
            var dispatch = new Dispatch(request);
            try
            {
                if (_dispatchMethods.TryGetValue(dispatch.Operation, out IceDMethod? dispatchMethod))
                {
                    (ReadOnlyMemory<ReadOnlyMemory<byte>> responsePayload, IStreamParamSender? streamParamSender) =
                        await dispatchMethod(this, request, dispatch, cancel).ConfigureAwait(false);

                    return new OutgoingResponse(request.Protocol, ResultType.Success)
                    {
                        Features = dispatch.ResponseFeatures,
                        Payload = responsePayload,
                        PayloadEncoding = request.PayloadEncoding,
                        StreamParamSender = streamParamSender,
                    };
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
