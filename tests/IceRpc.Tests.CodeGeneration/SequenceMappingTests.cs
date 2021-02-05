using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.CodeGeneration
{
    class SequenceMappingTests : ColocatedTest
    {
        ISequenceMappingTestServicePrx Prx { get; }
        public SequenceMappingTests() =>
            Prx = ObjectAdapter.AddWithUUID(new TestService(), ISequenceMappingTestServicePrx.Factory);

        [Test]
        public async Task SequenceMapping_GenericList()
        {
            var i = Enumerable.Range(0, 256).Select(x => (byte)x).ToList();
            var (r1, r2) = await Prx.OpLByteSAsync(i);
            CollectionAssert.AreEqual(i, r1);
            CollectionAssert.AreEqual(i, r2);
        }

        public class TestService : IAsyncSequenceMappingTestService
        {
            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpLByteSAsync(
                List<byte> i,
                Current current,
                CancellationToken cancel) => new((i, i));
        }
    }
}
