// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Slice
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
        [TestCase("OpMyCompactStruct")]
        [TestCase("OpMyClassA")]
        [TestCase("OpByteSeq")]
        [TestCase("OpStringList")]
        [TestCase("OpMyEnumDict")]
        [TestCase("OpMyEnumDict2")]
        public void Alias_OperationDeclarations(string baseName)
        {
            DeclarationsAreEquals(typeof(IAliasOperationsPrx).GetMethod($"{baseName}AAsync"),
                                  typeof(IAliasOperationsPrx).GetMethod($"{baseName}Async"));

            DeclarationsAreEquals(typeof(IAliasOperations).GetMethod($"{baseName}AAsync"),
                                  typeof(IAliasOperations).GetMethod($"{baseName}Async"));

            static void DeclarationsAreEquals(MethodInfo? m1, MethodInfo? m2)
            {
                Assert.That(m1, Is.Not.Null);
                Assert.That(m2, Is.Not.Null);

                ParameterInfo[] m1Params = m1.GetParameters();
                ParameterInfo[] m2Params = m1.GetParameters();

                Assert.That(m2Params.Length, Is.EqualTo(m1Params.Length));

                for (int i = 0; i < m1Params.Length; i++)
                {
                    ParameterInfo p1 = m1Params[i];
                    ParameterInfo p2 = m2Params[i];

                    Assert.That(p2.GetType(), Is.EqualTo(p1.GetType()));
                }
                Assert.That(m2.ReturnType, Is.EqualTo(m1.ReturnType));
            }
        }

        [TestCase(typeof(Alias.MyClass), typeof(Alias.MyClassA))]
        [TestCase(typeof(Alias.MyStruct), typeof(Alias.MyStructA))]
        public void Alias_DataMembers(Type t1, Type t2)
        {
            MemberInfo[] t1Members = t1.GetMembers();
            MemberInfo[] t2Members = t2.GetMembers();

            Assert.That(t2Members.Length, Is.EqualTo(t1Members.Length));

            for (int i = 0; i < t1Members.Length; i++)
            {
                MemberInfo m1 = t1Members[i];
                MemberInfo m2 = t2Members[i];
                Assert.That(m2.GetType(), Is.EqualTo(m1.GetType()));
            }
        }
    }
}
