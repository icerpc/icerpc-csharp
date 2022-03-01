// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Check that a string using the Ice proxy format can be parse as a proxy, converted to a string and
    /// parse again as the same proxy.</summary>
    /// <param name="str"></param>
    /// <param name="path"></param>
    /// <param name="fragment"></param>
    [TestCase("ice -t:tcp -h localhost -p 10000")]
    [TestCase("icerpc:ssl -h localhost -p 10000")]
    [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
    [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
    [TestCase("identity:coloc -h *")]
    [TestCase("identity -e 4.5:coloc -h *")]
    [TestCase("name -f facet:coloc -h localhost")]
    [TestCase("category/name -f facet:coloc -h localhost")]
    [TestCase("cat$gory/nam$ -f fac$t:coloc -h localhost")]
    [TestCase("\\342\\x82\\254\\60\\x9\\60\\")]
    [TestCase("bar/foo")]
    [TestCase("foo")]
    public void Parse_a_proxy_string_with_ice_format(string str)
    {
        var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);
        var proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.Default), format: IceProxyFormat.Default);

        Assert.That(proxy2, Is.EqualTo(proxy)); // round-trip works
    }

    /// <summary>Check that a string using the URI proxy format can be parse as a proxy, converted to a string and
    /// parse again as the same proxy.</summary>
    /// <param name="str">The string to parse.</param>
    /// <param name="path">The expected <see cref="Proxy.Path"/> for the proxy or null.</param>
    /// <param name="fragment">The expected <see cref="Proxy.Fragment"/> for the proxy or null.</param>
    [TestCase("icerpc://host.zeroc.com/path?encoding=foo")]
    [TestCase("ice://host.zeroc.com/identity#facet")]
    [TestCase("ice://host.zeroc.com/identity#facet#?!$x")]
    [TestCase("ice://host.zeroc.com/identity#")]
    [TestCase("ice://host.zeroc.com/identity#%24%23f")]
    [TestCase("ice://host.zeroc.com/identity?tls=false")]
    [TestCase("ice://host.zeroc.com/identity?tls=true")]
    [TestCase("ice:/path?adapter-id=foo")]
    [TestCase("icerpc://host.zeroc.com:1000/category/name")]
    [TestCase("icerpc://host.zeroc.com:1000/loc0/loc1/category/name")]
    [TestCase("icerpc://host.zeroc.com/category/name%20with%20space")]
    [TestCase("icerpc://host.zeroc.com/category/name with space")]
    [TestCase("icerpc://host.zeroc.com//identity")]
    [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
    [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
    [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
    [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
    [TestCase("icerpc://[::1]/path?alt-endpoint=host1?adapter-id=foo=bar$name=value&alt-endpoint=host2?foo=bar$123=456")]
    [TestCase("ice:/location/identity#facet")]
    [TestCase("ice:///location/identity#facet")] // we tolerate an empty host
    [TestCase("icerpc://host.zeroc.com//identity")]
    [TestCase("ice://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F")]
    // TODO: add test with # in fragment
    [TestCase("ice://host.zeroc.com/identity#\x7f€$%/!$'()*+,:;=@[] %2F")]
    [TestCase(@"icerpc://host.zeroc.com/foo\bar\n\t!")] // \ becomes /
    // another syntax for empty port
    [TestCase("icerpc://host.zeroc.com:/identity")]
    [TestCase("icerpc://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
    [TestCase("icerpc://host.zeroc.com/identity?transport=100")]
    // leading :: to make the address IPv6-like
    [TestCase("icerpc://[::ab:cd:ef:00]/identity?transport=bt")]
    [TestCase("icerpc://host.zeroc.com:10000/identity?transport=tcp")]
    [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
    [TestCase("icerpc://mylocation.domain.com/foo/bar?transport=loc")]
    [TestCase("icerpc://host:10000?transport=coloc")]
    [TestCase("icerpc:/tcp -p 10000")] // not recommended
    [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
    [TestCase("ice://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
    [TestCase("ice://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
    // IDN
    [TestCase("icerpc://München-Ost:10000/path")]
    [TestCase("icerpc://xn--mnchen-ost-9db.com/path")]
    // relative proxies
    [TestCase("/foo/bar")]
    [TestCase("//foo/bar")]
    [TestCase("/foo:bar")]
    // non-supported protocols
    [TestCase("foobar://host:10000/path")]
    [TestCase("foobar://host/path#fragment")]
    [TestCase("foobar:path")]  // not a valid path since it doesn't start with /, and that's ok
    [TestCase("foobar:path#fragment")]
    public void Parse_a_proxy_string_with_uri_format(string str)
    {
        var proxy = Proxy.Parse(str);
        string str2 = proxy.ToString();

        var proxy2 = Proxy.Parse(str2);

        Assert.That(proxy2, Is.EqualTo(proxy));
    }

    /// <summary>Tests that parsing an invalid proxy fails with <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    [TestCase("")]
    [TestCase("\"\"")]
    [TestCase("icerpc://host/path?alt-endpoint=")] // alt-endpoint authority cannot be empty
    [TestCase("icerpc://host/path?alt-endpoint=/foo")] // alt-endpoint cannot have a path
    [TestCase("icerpc://host/path?alt-endpoint=icerpc://host")] // alt-endpoint cannot have a scheme
    [TestCase("icerpc:path")]                  // bad path
    [TestCase("icerpc:/host/path#fragment")]   // bad fragment
    [TestCase("icerpc:/path#fragment")]        // bad fragment
    [TestCase("icerpc://user@host/path")]      // bad user info
    [TestCase("ice://host/s1/s2/s3")]          // too many slashes in path
    [TestCase("ice://host/cat/")]              // empty identity name
    [TestCase("ice://host/")]                  // empty identity name
    [TestCase("ice://host//")]                 // empty identity name
    [TestCase("ice:/path?alt-endpoint=foo")]   // alt-endpoint proxy parameter
    [TestCase("ice:/path?adapter-id")]         // empty adapter-id
    [TestCase("ice:/path?adapter-id=foo&foo")] // extra parameter
    public void Parse_a_proxy_string_with_invalid_uri_format(string str) =>
        Assert.Catch<FormatException>(() => Proxy.Parse(str));

    /// <summary>Tests that parsing an invalid proxy fails with <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host
    [TestCase("")]
    [TestCase("\"\"")]
    [TestCase("\"\" test")] // invalid trailing characters
    [TestCase("id@server test")]
    [TestCase("id -e A.0:tcp -h foobar")]
    [TestCase("id -f \"facet x")]
    [TestCase("id -f \'facet x")]
    [TestCase("test -f facet@test @test")]
    [TestCase("test -p 2.0")]
    [TestCase("xx\01FooBar")] // Illegal character < 32
    [TestCase("xx\\ud911")] // Illegal surrogate
    [TestCase("test/foo/bar")]
    [TestCase("cat//test")]
    [TestCase("cat/")] // Empty name
    public void Parse_a_proxy_string_with_invalid_ice_format(string str) =>
        Assert.Throws<FormatException>(() => Proxy.Parse(str, format: IceProxyFormat.Default));
}
