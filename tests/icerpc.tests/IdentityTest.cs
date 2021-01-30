using System;
using Xunit;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests
{
    public class IdentityTest
    {
        /// <summary>Test Identity to string conversion.</summary>
        /// <param name="id">The identity to convert to a string.</param>
        /// <param name="expected">The expected result.</param>
        [Theory]
        [ClassData(typeof(ToStringData))]
        public void TestToString(Identity id, string expected)
        {
            Assert.Equal(expected, id.ToString());
            Assert.Equal(id, Identity.Parse(expectedes, uriFormat: true));
        }

        public class ToStringData : TheoryData<Identity, string>
        {
            public ToStringData()
            {
                Add(new Identity("test", "\x7f€"), "%7F%E2%82%AC/test");
                Add(new Identity("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a"),
                                 "greek%20%F0%90%85%AA/banana%20%0E-%F0%9F%8D%8C%E2%82%AC%C2%A2%24");
            }
        }

        /// <summary>Test Identity to string conversion using the specified ToStringMode.</summary>
        /// <param name="id">The identity to convert to a string.</param>
        /// <param name="mode">The mode argument to call ToString</param>
        /// <param name="expected">The expected result for ToString invocation.</param>
        [Theory]
        [ClassData(typeof(ToStringModeData))]
        public void TestToStringMode(Identity id, ToStringMode mode, string expected)
        {
            Assert.Equal(expected, id.ToString(mode));
            Assert.Equal(id, Identity.Parse(expected, uriFormat: false));
        }

        public class ToStringModeData : TheoryData<Identity, ToStringMode, string>
        {
            public ToStringModeData()
            {
                var id = new Identity("test", "\x7f€");
                Add(id, ToStringMode.Unicode, "\\u007f€/test");
                Add(id, ToStringMode.ASCII, "\\u007f\\u20ac/test");
                Add(id, ToStringMode.Compat, "\\177\\342\\202\\254/test");

                id = new Identity("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a");
                Add(id, ToStringMode.Unicode, "greek \ud800\udd6a/banana \\u000e-\ud83c\udf4c\u20ac\u00a2$");
                Add(id, ToStringMode.ASCII, "greek \\U0001016a/banana \\u000e-\\U0001f34c\\u20ac\\u00a2$");
                Add(id,
                    ToStringMode.Compat,
                    "greek \\360\\220\\205\\252/banana \\016-\\360\\237\\215\\214\\342\\202\\254\\302\\242$");

                // escaped escapes in Identity
                id = new Identity("test", ",X2QNUAzSBcJ_e$AV;E\\");
                Add(id, ToStringMode.Unicode, ",X2QNUAzSBcJ_e$AV;E\\\\/test");

                id = new Identity("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\");
                Add(id, ToStringMode.Unicode, ",X2QNUAz\\\\SB\\\\\\/cJ_e$AV;E\\\\\\\\/test");

                id = new Identity("/test", "cat/");
                Add(id, ToStringMode.Unicode, "cat\\//\\/test");

                // Input string in ice1 format with various pitfalls
                id = new Identity("\\342\\x82\\254\\60\\x9\\60\\", "");
                Add(id, ToStringMode.Unicode, "€0\t0\\");
            }
        }

        /// <summary>Identity.Parse for an invalid identity throws FormatException, Identity.TryParse
        /// for an invalid identity must return false.</summary>
        [Theory]
        // Illegal character < 32
        [InlineData("xx\01FooBar")]
        // Illegal surrogate
        [InlineData("xx\\ud911")]
        [InlineData("test/foo/bar")]
        [InlineData("cat//test")]
        public void ParseInvalidIdentity(string str)
        {
            Assert.Throws<FormatException>(() => Identity.Parse(str, uriFormat: false));
            Assert.False(Identity.TryParse(str, uriFormat: false, out _));
        }
    }
}
