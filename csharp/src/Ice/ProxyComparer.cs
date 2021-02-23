// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace ZeroC.Ice
{
    /// <summary>Represents an <see cref="IServicePrx">object proxy</see>comparison operation based on all or only some
    /// of the proxy properties. The <see cref="EqualityComparer{T}.Default"/> property delegates to the implementation
    /// of <see cref="IEquatable{T}"/> provided by IObjectPrx.</summary>
    public abstract class ProxyComparer : EqualityComparer<IServicePrx>
    {
        /// <summary>Gets a <see cref="ProxyComparer"/> that compares proxies based only on the proxies' object
        /// identity.</summary>
        public static ProxyComparer Identity { get; } = new IdentityComparer();

        /// <summary>Gets a <see cref="ProxyComparer"/> that compares proxies based only on the proxies' object identity
        /// and facet.</summary>
        public static ProxyComparer IdentityAndFacet { get; } = new IdentityAndFacetComparer();

        private class IdentityComparer : ProxyComparer
        {
            public override int GetHashCode(IServicePrx obj) => obj.Identity.GetHashCode();

            public override bool Equals(IServicePrx? lhs, IServicePrx? rhs)
            {
                if (ReferenceEquals(lhs, rhs))
                {
                    return true;
                }

                if (lhs == null || rhs == null)
                {
                    return false;
                }

                return lhs.Identity == rhs.Identity;
            }
        }

        private class IdentityAndFacetComparer : ProxyComparer
        {
            public override int GetHashCode(IServicePrx obj) => HashCode.Combine(obj.Identity, obj.Facet);

            public override bool Equals(IServicePrx? lhs, IServicePrx? rhs)
            {
                if (ReferenceEquals(lhs, rhs))
                {
                    return true;
                }

                if (lhs == null || rhs == null)
                {
                    return false;
                }

                return lhs.Identity == rhs.Identity && lhs.Facet == rhs.Facet;
            }
        }
    }
}
