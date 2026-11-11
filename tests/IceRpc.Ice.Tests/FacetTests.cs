// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class FacetTests
{
    [TestCase("foo", "foo")]
    [TestCase("a-._~!$&'()*+,;=:@/?z", "a-._~!$&'()*+,;=:@/?z")]
    [TestCase("\"%<>#[\\]^`{|} ", "%22%25%3C%3E%23%5B%5C%5D%5E%60%7B%7C%7D%20")]
    [TestCase("\x7f€", "%7F%E2%82%AC")]
    public void Convert_a_facet_to_a_fragment(string facet, string expectedFragment)
    {
        Assert.That(new List<string> { facet }.ToFragment(), Is.EqualTo(expectedFragment));
        Assert.That(expectedFragment.ToFacet(), Is.EqualTo(new List<string> { facet }));
    }

    [Test]
    public void Convert_an_empty_facet_to_an_empty_fragment()
    {
        Assert.That(new List<string>().ToFragment(), Is.Empty);
        Assert.That("".ToFacet(), Is.Empty);
    }

    /// <summary>Verifies that converting a fragment that is not produced by <c>ToFragment</c> into a facet fails.
    /// </summary>
    [TestCase("a%3Ab")] // escaped character that is valid unescaped
    [TestCase("a%2Fb")] // escaped character that is valid unescaped
    [TestCase("a%41b")] // escaped unreserved character
    [TestCase("a%3cb")] // lowercase hex digits
    [TestCase("a%3")] // truncated escape
    [TestCase("%FF")] // escaped bytes that are not UTF-8
    [TestCase("a[b]")] // character that is not valid unescaped in a fragment
    [TestCase("a#b")]
    [TestCase("a b")]
    [TestCase("é")]
    public void Convert_a_bad_fragment_to_a_facet_fails(string fragment) =>
        Assert.That(() => fragment.ToFacet(), Throws.TypeOf<FormatException>());
}
