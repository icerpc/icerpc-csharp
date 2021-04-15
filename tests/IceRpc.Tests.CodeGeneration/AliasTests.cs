// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Reflection;

namespace IceRpc.Tests.CodeGeneration
{
    // Tests in this class ensure that definitions using alias are equals to definitions without alias,
    // we duplicate the Slice definitions using alias and then use introspection to check that the generated
    // code for both use the same types.
    public class AliasTests
    {
        [TestCase("OpBool")]
        [TestCase("OpByte")]
        [TestCase("OpShort")]
        [TestCase("OpUShort")]
        [TestCase("OpInt")]
        [TestCase("OpUInt")]
        [TestCase("OpVarint")]
        [TestCase("OpVaruint")]
        [TestCase("OpLong")]
        [TestCase("OpULong")]
        [TestCase("OpVarlong")]
        [TestCase("OpVarulong")]
        [TestCase("OpFloat")]
        [TestCase("OpDouble")]
        [TestCase("OpString")]
        [TestCase("OpMyEnum")]
        [TestCase("OpMyStruct")]
        [TestCase("OpMyClassA")]
        [TestCase("OpByteSeq")]
        [TestCase("OpStringList")]
        [TestCase("OpMyEnumDict")]
        [TestCase("OpMyEnumDict2")]
        public void Alias_OperationDeclarations(string baseName)
        {
            DeclarationsAreEquals(typeof(IAliasOperationsPrx).GetMethod($"{baseName}AAsync"),
                                  typeof(IAliasOperationsPrx).GetMethod($"{baseName}Async"));

            DeclarationsAreEquals(typeof(IAsyncAliasOperations).GetMethod($"{baseName}AAsync"),
                                  typeof(IAsyncAliasOperations).GetMethod($"{baseName}Async"));

            static void DeclarationsAreEquals(MethodInfo? m1, MethodInfo? m2)
            {
                Assert.IsNotNull(m1);
                Assert.IsNotNull(m2);

                ParameterInfo[] m1Params = m1.GetParameters();
                ParameterInfo[] m2Params = m1.GetParameters();

                Assert.AreEqual(m1Params.Length, m2Params.Length);

                for (int i = 0; i < m1Params.Length; i++)
                {
                    ParameterInfo p1 = m1Params[i];
                    ParameterInfo p2 = m2Params[i];

                    Assert.AreEqual(p1.GetType(), p2.GetType());
                }
                Assert.AreEqual(m1.ReturnType, m2.ReturnType);
            }
        }

        [TestCase(typeof(Alias.MyClass), typeof(Alias.MyClassA))]
        [TestCase(typeof(Alias.MyStruct), typeof(Alias.MyStructA))]
        public void Alias_DataMembers(Type t1, Type t2)
        {
            MemberInfo[] t1Members = t1.GetMembers();
            MemberInfo[] t2Members = t2.GetMembers();

            Assert.AreEqual(t1Members.Length, t2Members.Length);

            for (int i = 0; i < t1Members.Length; i++)
            {
                MemberInfo m1 = t1Members[i];
                MemberInfo m2 = t2Members[i];
                Assert.AreEqual(m1.GetType(), m2.GetType());
            }
        }
    }
}