
// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    /// <summary>Factory class for proxies.</summary>
    /// <typeparam name="T">The proxy type.</typeparam>
    public sealed class ProxyFactory<T> where T : class, IServicePrx
    {
        /// <summary>Returns the default path for this proxy type.</summary>
        public string DefaultPath => _defaultPath ??= typeof(T).GetDefaultPath();

        private string? _defaultPath;
        private readonly Func<string, Protocol, T> _function;

        /// <summary>Constructs a proxy factory.</summary>
        /// <param>The function that <see cref="Create"/> calls to create proxies.</param>
        public ProxyFactory(Func<string, Protocol, T> function) => _function = function;

        /// <summary>Creates a new proxy.</summary>
        /// <param name="path">The path of the new proxy.</param>
        /// <param name="protocol">The protocol of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        public T Create(string path, Protocol protocol) => _function(path, protocol);
    }
}
