using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZeroC.Ice;

namespace IceRpc.Tests.Encoding
{
    class BuiltInTypesTests
    {
        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_V11_Integer(int p1)
        {
            var data = new List<ArraySegment<byte>>() { new byte[256] };
            var ostr = new OutputStream(ZeroC.Ice.Encoding.V11, data);
            ostr.WriteInt(p1);

            var istr = new InputStream(data[0], ZeroC.Ice.Encoding.V11);
            int r1 = istr.ReadInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, ostr.Tail.Segment);
            Assert.AreEqual(4, ostr.Tail.Offset);
            Assert.AreEqual(4, istr.Pos);
        }

        [TestCase(long.MinValue)]
        [TestCase(0)]
        [TestCase(long.MaxValue)]
        public void Encoding_V11_Long(long p1)
        {
            var data = new List<ArraySegment<byte>>() { new byte[256] };
            var ostr = new OutputStream(ZeroC.Ice.Encoding.V11, data);
            ostr.WriteLong(p1);

            var istr = new InputStream(data[0], ZeroC.Ice.Encoding.V11);
            long r1 = istr.ReadLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, ostr.Tail.Segment);
            Assert.AreEqual(8, ostr.Tail.Offset);
            Assert.AreEqual(8, istr.Pos);
        }
    }
}
