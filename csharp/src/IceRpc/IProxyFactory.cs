// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    public interface IProxyFactory<T> where T : class, IServicePrx
    {
        /// <summary>Creates a new service proxy.</summary>
        /// <param name="options">The service proxy options.</param>
        /// <returns>The new service proxy.</returns>
        T Create(ServicePrxOptions options);
    }
}
