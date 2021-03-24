// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class EnumTests
    {
        [Test]
        public void Enum_Values()
        {
            // Ensure the generate enum has the expected values
            Assert.AreEqual(0, (int)MyEnum.enum1);
            Assert.AreEqual(1, (int)MyEnum.enum2);
            Assert.AreEqual(10, (int)MyEnum.enum3);
            Assert.AreEqual(11, (int)MyEnum.enum4);
            Assert.AreEqual(20, (int)MyEnum.enum5);
            Assert.AreEqual(21, (int)MyEnum.enum6);
            Assert.AreEqual(30, (int)MyEnum.enum7);
            Assert.AreEqual(31, (int)MyEnum.enum8);
            Assert.AreEqual(40, (int)MyEnum.enum9);
            Assert.AreEqual(41, (int)MyEnum.enum10);
            Assert.AreEqual(226, (int)MyEnum.enum11);

            Assert.AreEqual(sizeof(short), sizeof(MyFixedLengthEnum));

            Assert.AreEqual(-3, (short)MyFixedLengthEnum.senum1);
            Assert.AreEqual(-2, (short)MyFixedLengthEnum.senum2);
            Assert.AreEqual(10, (short)MyFixedLengthEnum.senum3);
            Assert.AreEqual(11, (short)MyFixedLengthEnum.senum4);
            Assert.AreEqual(20, (short)MyFixedLengthEnum.senum5);
            Assert.AreEqual(21, (short)MyFixedLengthEnum.senum6);
            Assert.AreEqual(30, (short)MyFixedLengthEnum.senum7);
            Assert.AreEqual(31, (short)MyFixedLengthEnum.senum8);
            Assert.AreEqual(40, (short)MyFixedLengthEnum.senum9);
            Assert.AreEqual(41, (short)MyFixedLengthEnum.senum10);
            Assert.AreEqual(32766, (short)MyFixedLengthEnum.senum11);

            Assert.AreEqual(sizeof(uint), sizeof(MyUncheckedEnum));

            Assert.AreEqual(1, (uint)MyUncheckedEnum.E0);
            Assert.AreEqual(2, (uint)MyUncheckedEnum.E1);
            Assert.AreEqual(4, (uint)MyUncheckedEnum.E2);
            Assert.AreEqual(8, (uint)MyUncheckedEnum.E3);
            Assert.AreEqual(16, (uint)MyUncheckedEnum.E4);
            Assert.AreEqual(32, (uint)MyUncheckedEnum.E5);
            Assert.AreEqual(64, (uint)MyUncheckedEnum.E6);
            Assert.AreEqual(128, (uint)MyUncheckedEnum.E7);
            Assert.AreEqual(256, (uint)MyUncheckedEnum.E8);
            Assert.AreEqual(512, (uint)MyUncheckedEnum.E9);
            Assert.AreEqual(1024, (uint)MyUncheckedEnum.E10);
        }
    }
}
