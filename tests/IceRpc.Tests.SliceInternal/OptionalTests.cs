// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.SliceInternal
{
    public class OptionalTests
    {
        /// <summary>Test that an optional proxy can be encoded with both 1.1 and 2.0 encodings.</summary>
        [TestCase("icerpc://host.zeroc.com/hello")]
        [TestCase(null)]

        public void Optional_Members(string? prxStr)
        {
            var myOptional = new MyOptional("str", prxStr == null ? null : ServicePrx.Parse(prxStr));
            var buffer = new byte[256];

            EncodePayload(SliceEncoding.Slice20, myOptional);
            Assert.That(myOptional, Is.EqualTo(DecodePayload(SliceEncoding.Slice20)));

            EncodePayload(SliceEncoding.Slice11, myOptional);
            Assert.That(myOptional, Is.EqualTo(DecodePayload(SliceEncoding.Slice11)));

            void EncodePayload(SliceEncoding encoding, MyOptional value)
            {
                var encoder = new SliceEncoder(new SingleBufferWriter(buffer), encoding);
                value.Encode(ref encoder);
            }

            MyOptional DecodePayload(SliceEncoding encoding)
            {
                var decoder = new SliceDecoder(buffer, encoding);
                return new MyOptional(ref decoder);
            }
        }
    }
}
