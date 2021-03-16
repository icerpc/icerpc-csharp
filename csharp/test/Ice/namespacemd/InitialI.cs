// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.NamespaceMD
{
    public class Initial : IInitial
    {
        public NoNamespace.C1 GetNoNamespaceC2AsC1(Current current, CancellationToken cancel) =>
            new NoNamespace.C2();

        public NoNamespace.C2 GetNoNamespaceC2AsC2(Current current, CancellationToken cancel) =>
            new NoNamespace.C2();

        public NoNamespace.N1.N2.S1 GetNoNamespaceN1N2S1(Current current, CancellationToken cancel) =>
            new NoNamespace.N1.N2.S1();

        public WithNamespace.C1 GetWithNamespaceC2AsC1(Current current, CancellationToken cancel) =>
            new WithNamespace.C2();

        public WithNamespace.C2 GetWithNamespaceC2AsC2(Current current, CancellationToken cancel) =>
            new WithNamespace.C2();

        public WithNamespace.N1.N2.S1 GetWithNamespaceN1N2S1(Current current, CancellationToken cancel) =>
            new WithNamespace.N1.N2.S1();

        public M1.M2.M3.S2 GetNestedM0M2M3S2(Current current, CancellationToken cancel) =>
            new M1.M2.M3.S2();

        public void Shutdown(Current current, CancellationToken cancel) =>
            current.Server.ShutdownAsync();

        public void ThrowNoNamespaceE2AsE1(Current current, CancellationToken cancel) => throw new NoNamespace.E2();

        public void ThrowNoNamespaceE2AsE2(Current current, CancellationToken cancel) => throw new NoNamespace.E2();

        public void ThrowNoNamespaceNotify(Current current, CancellationToken cancel) =>
            throw new NoNamespace.@notify();

        public void ThrowWithNamespaceE2AsE1(Current current, CancellationToken cancel) =>
            throw new WithNamespace.E2();

        public void ThrowWithNamespaceE2AsE2(Current current, CancellationToken cancel) =>
            throw new WithNamespace.E2();
    }
}
