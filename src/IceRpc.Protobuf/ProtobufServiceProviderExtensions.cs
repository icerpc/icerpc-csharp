// Copyright (c) ZeroC, Inc.

// TODO: temporary, for paramref. See #4220.
#pragma warning disable CS1734 // XML comment has a type parameter reference that is not valid.

namespace IceRpc.Protobuf;

/// <summary>Provides extension methods for <see cref="IServiceProvider" /> to create Protobuf clients.</summary>
public static class ProtobufServiceProviderExtensions
{
    /// <summary>Extension methods for <see cref="IServiceProvider" />.</summary>
    /// <param name="provider">The service provider.</param>
    extension(IServiceProvider provider)
    {
        /// <summary>Creates a Protobuf client with this service provider.</summary>
        /// <typeparam name="TClient">The Protobuf client struct.</typeparam>
        /// <param name="serviceAddress">The service address of the new client; null is equivalent to the default
        /// service address for the client type.</param>
        /// <returns>A new instance of <typeparamref name="TClient" />.</returns>
        /// <remarks>The new client uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" />
        /// as its invocation pipeline, and the <see cref="ProtobufEncodeOptions" /> retrieved from
        /// <paramref name="provider" /> as its encode options.</remarks>
        public TClient CreateProtobufClient<TClient>(ServiceAddress? serviceAddress = null)
            where TClient : struct, IProtobufClient
        {
            var invoker = (IInvoker?)provider.GetService(typeof(IInvoker));
            if (invoker is null)
            {
                throw new InvalidOperationException(
                    "Could not find service of type 'IInvoker' in the service container.");
            }

            return serviceAddress is null ?
                new TClient
                {
                    EncodeOptions = (ProtobufEncodeOptions?)provider.GetService(typeof(ProtobufEncodeOptions)),
                    Invoker = invoker
                }
                :
                new TClient
                {
                    EncodeOptions = (ProtobufEncodeOptions?)provider.GetService(typeof(ProtobufEncodeOptions)),
                    Invoker = invoker,
                    ServiceAddress = serviceAddress
                };
        }

        /// <summary>Creates a Protobuf client with this service provider.</summary>
        /// <typeparam name="TClient">The Protobuf client struct.</typeparam>
        /// <param name="serviceAddressUri">The service address of the client as a URI.</param>
        /// <returns>A new instance of <typeparamref name="TClient" />.</returns>
        /// <remarks>The new client uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" />
        /// as its invocation pipeline, and the <see cref="ProtobufEncodeOptions" /> retrieved from
        /// <paramref name="provider" /> as its encode options.</remarks>
        public TClient CreateProtobufClient<TClient>(Uri serviceAddressUri)
            where TClient : struct, IProtobufClient =>
            provider.CreateProtobufClient<TClient>(new ServiceAddress(serviceAddressUri));
    }
}
