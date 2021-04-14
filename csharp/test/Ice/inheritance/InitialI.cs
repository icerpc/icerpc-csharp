// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test.Inheritance.MA;
using IceRpc.Test.Inheritance.MB;
using System.Threading;

namespace IceRpc.Test.Inheritance
{
    public sealed class InitialI : IInitial
    {
        private readonly IAPrx _ia;
        private readonly IB1Prx _ib1;
        private readonly IB2Prx _ib2;
        private readonly ICPrx _ic;

        public InitialI(Server server)
        {
            _ia = TestHelper.AddWithGuid<IAPrx>(server, new A());
            _ib1 = TestHelper.AddWithGuid<IB1Prx>(server, new B1());
            _ib2 = TestHelper.AddWithGuid<IB2Prx>(server, new B2());
            _ic = TestHelper.AddWithGuid<ICPrx>(server, new C());
        }

        public IAPrx Iaop(Current current, CancellationToken cancel) => _ia;
        public IB1Prx Ib1op(Current current, CancellationToken cancel) => _ib1;

        public IB2Prx Ib2op(Current current, CancellationToken cancel) => _ib2;

        public ICPrx Icop(Current current, CancellationToken cancel) => _ic;

        public void Shutdown(Current current, CancellationToken cancel) =>
            current.Server.ShutdownAsync();
    }
}
