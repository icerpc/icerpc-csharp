// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class IdentityTests
{
    /// <summary>Verifies that any arbitrary Ice Identity can represented by a URI path.</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    /// <param name="referencePath">The normalized URI path to check against.</param>
    [TestCase("foo", "bar", "/bar/foo")]
    [TestCase("foo", "", "/foo")]
    [TestCase("", "", "/")]
    [TestCase("", "bar", "/")]
    [TestCase("test", "\x7f€", "/%7F%E2%82%AC/test")]
    [TestCase(
        "banana \x0E-🍌€¢$",
        "greek 𐅪",
        "/greek%20%F0%90%85%AA/banana%20%0E-%F0%9F%8D%8C%E2%82%AC%C2%A2$")]
    [TestCase("/foo", "", "/%2Ffoo")] // cSpell:disable-line
    [TestCase("/foo", "bar", "/bar/%2Ffoo")] // cSpell:disable-line
    [TestCase("/foo", "/bar/", "/%2Fbar%2F/%2Ffoo")] // cSpell:disable-line
    [TestCase("foo/// ///#@", "/bar/", "/%2Fbar%2F/foo%2F%2F%2F%20%2F%2F%2F%23@")] // cSpell:disable-line
    [TestCase("a-._~!$&'()*+,;=:@z", "AZ09", "/AZ09/a-._~!$&'()*+,;=:@z")]
    [TestCase("\"%<>?[\\]^`{|}", "", "/%22%25%3C%3E%3F%5B%5C%5D%5E%60%7B%7C%7D")]
    public void Convert_an_identity_to_uri_path(string name, string category, string referencePath)
    {
        var path = new Identity(name, category).ToPath();

        Assert.That(path, Is.EqualTo(referencePath));
    }

    /// <summary>Verifies that parsing the path of an identity returns this identity.</summary>
    [TestCase("foo", "")]
    [TestCase("foo", "bar")]
    [TestCase("a:b", "c@t")]
    [TestCase("a/b", "/")]
    [TestCase("banana \x0E-🍌€¢$", "greek 𐅪")]
    [TestCase("\"%<>?[\\]^`{|}#", "")]
    public void Parse_identity_path_returns_the_identity(string name, string category)
    {
        var identity = new Identity(name, category);

        Assert.That(Identity.Parse(identity.ToPath()), Is.EqualTo(identity));
    }

    [Test]
    public void Parse_root_path_returns_the_null_identity() =>
        Assert.That(Identity.Parse("/"), Is.EqualTo(new Identity("", "")));

    /// <summary>Verifies that parsing a path that is not the path of an identity, as produced by
    /// <see cref="Identity.ToPath" />, fails.</summary>
    [TestCase("")]
    [TestCase("foo")]
    [TestCase("/a/b/c")]
    [TestCase("//b")]
    [TestCase("/foo/")]
    [TestCase("//")]
    [TestCase("/a%3Ab")] // escaped character that is valid unescaped
    [TestCase("/a%41b")] // escaped unreserved character
    [TestCase("/a%2fb")] // lowercase hex digits
    [TestCase("/a%2")] // truncated escape
    [TestCase("/a%zz")] // not hex digits
    [TestCase("/%FF")] // escaped bytes that are not UTF-8
    [TestCase("/%C3")] // truncated UTF-8 sequence
    [TestCase("/%C0%AF")] // overlong UTF-8 encoding of '/'
    [TestCase("/%ED%A0%80")] // UTF-8 encoding of a surrogate code point
    [TestCase("/a[b]")] // character that is not valid unescaped in a path segment
    [TestCase("/a b")]
    [TestCase("/a#b")]
    [TestCase("/a?b")]
    [TestCase("/é")]
    public void Parse_bad_path_fails(string path) =>
        Assert.That(() => Identity.Parse(path), Throws.TypeOf<FormatException>());
}
