using System;
using System.Collections;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Ice.Tests
{
    public class IdentityTest
    {
        /// <summary>Test Identity to string conversion.</summary>
        /// <param name="id">The identity to convert to a string.</param>
        /// <param name="expected">The expected result.</param>
        [TestCaseSource(typeof(ToStringTestCases))]
        public void TestToString(Identity id, string expected)
        {
            Assert.AreEqual(expected, id.ToString());
            Assert.AreEqual(id, Identity.Parse(expected, uriFormat: true));
        }

        public class ToStringTestCases : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                yield return new object[] { new Identity("test", "\x7f€"), "%7F%E2%82%AC/test" };
                yield return new object[] 
                {
                    new Identity("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a"),
                    "greek%20%F0%90%85%AA/banana%20%0E-%F0%9F%8D%8C%E2%82%AC%C2%A2%24"
                };
            }
        }

        /// <summary>Test Identity to string conversion using the specified ToStringMode.</summary>
        /// <param name="id">The identity to convert to a string.</param>
        /// <param name="mode">The mode argument to call ToString</param>
        /// <param name="expected">The expected result for ToString invocation.</param>
        [TestCaseSource(typeof(ToStringModeTestCases))]
        public void TestToStringMode(Identity id, ToStringMode mode, string expected)
        {
            Assert.AreEqual(expected, id.ToString(mode));
            Assert.AreEqual(id, Identity.Parse(expected, uriFormat: false));
        }

        class ToStringModeTestCases : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                var id = new Identity("test", "\x7f€");
                yield return new object[] { id, ToStringMode.Unicode, "\\u007f€/test" };
                yield return new object[] { id, ToStringMode.ASCII, "\\u007f\\u20ac/test" };
                yield return new object[] { id, ToStringMode.Compat, "\\177\\342\\202\\254/test" };

                id = new Identity("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a");
                yield return new object[]
                {
                    id,
                    ToStringMode.Unicode,
                    "greek \ud800\udd6a/banana \\u000e-\ud83c\udf4c\u20ac\u00a2$" 
                };

                yield return new object[]
                {
                    id,
                    ToStringMode.ASCII,
                    "greek \\U0001016a/banana \\u000e-\\U0001f34c\\u20ac\\u00a2$"
                };
                
                yield return new object[]
                {
                    id,
                    ToStringMode.Compat,
                    "greek \\360\\220\\205\\252/banana \\016-\\360\\237\\215\\214\\342\\202\\254\\302\\242$"
                };

                // escaped escapes in Identity
                id = new Identity("test", ",X2QNUAzSBcJ_e$AV;E\\");
                yield return new object[] { id, ToStringMode.Unicode, ",X2QNUAzSBcJ_e$AV;E\\\\/test" };

                id = new Identity("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\");
                yield return new object[] { id, ToStringMode.Unicode, ",X2QNUAz\\\\SB\\\\\\/cJ_e$AV;E\\\\\\\\/test" };

                id = new Identity("/test", "cat/");
                yield return new object[] { id, ToStringMode.Unicode, "cat\\//\\/test" };
            }
        }

        /// <summary>Identity.Parse for an invalid identity throws FormatException, Identity.TryParse
        /// for an invalid identity must return false.</summary>
        // Illegal character < 32
        [TestCase("xx\01FooBar")]
        // Illegal surrogate
        [TestCase("xx\\ud911")]
        [TestCase("test/foo/bar")]
        [TestCase("cat//test")]
        public void TestParseInvalidIdentity(string str)
        {
            Assert.Throws<FormatException>(() => Identity.Parse(str, uriFormat: false));
            Assert.False(Identity.TryParse(str, uriFormat: false, out _));
        }

         [TestCaseSource(typeof(ParseValidIdentityTestCases))]
        public void TestParseValidIdentity(Identity expected, string str, bool uriFormat)
        {
            Assert.AreEqual(expected, Identity.Parse(str, uriFormat));
        }

        public class ParseValidIdentityTestCases : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                // Input string in ice1 format with various pitfalls
                yield return new object[] { new Identity("€0\t0\\", ""), "\\342\\x82\\254\\60\\x9\\60\\", false };
            }
        }
    }
}
