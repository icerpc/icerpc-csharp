// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerAddressTests
{
    /// <summary>Provides test case data for
    /// <see cref="Create_server_address_from_valid_uri(Uri, string, ushort, string?, IDictionary{string, string})" />
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServerAddressUriSource
    {
        get
        {
            foreach ((Uri uri,
                      string host,
                      ushort port,
                      string? transport,
                      IDictionary<string, string>? parameters) in _validServerAddress)
            {
                yield return new TestCaseData(
                    uri,
                    host,
                    port,
                    transport,
                    parameters ?? new Dictionary<string, string>());
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_an_server_address_into_a_string(Uri)" /> test.</summary>
    private static IEnumerable<TestCaseData> ServerAddressToStringSource
    {
        get
        {
            foreach ((Uri uri, string _, ushort _, string? _, IDictionary<string, string>? _) in _validServerAddress)
            {
                yield return new TestCaseData(uri);
            }
        }
    }

    /// <summary>A collection of valid server address strings with its expected host, port, transport and parameters.
    /// </summary>
    private static readonly (Uri Uri, string Host, ushort Port, string? Transport, IDictionary<string, string>? Parameters)[] _validServerAddress =
        new (Uri, string, ushort, string?, IDictionary<string, string>?)[]
        {
            (new Uri("icerpc://host:10000"), "host", 10000, null, null),
            (new Uri("icerpc://host:10000?transport=foobar"), "host", 10000, "foobar", null),
            (new Uri("icerpc://host"), "host", 4062, null, null),
            (new Uri("icerpc://[::0]"), "::", 4062, null, null),
            (new Uri("ice://[::0]?foo=bar&xyz=true"),
             "::",
             4061,
             null,
             new Dictionary<string, string>() { ["foo"] = "bar", ["xyz"] = "true" }),
            (new Uri("icerpc://host:10000?transport=coloc"), "host", 10000, "coloc", null),
            (new Uri("ice://localhost?transport=tcp"), "localhost", 4061, "tcp", null),
            (new Uri("ice://host:10000"), "host", 10000, null, null),
            (new Uri("IceRpc://host:10000"), "host", 10000, null, null),
        };

    /// <summary>Verifies that a server address can be correctly converted into a string.</summary>
    /// <param name="uri1">The server address URI to test.</param>
    [Test]
    [TestCaseSource(nameof(ServerAddressToStringSource))]
    public void Convert_an_server_address_into_a_string(Uri uri1)
    {
        var serverAddress1 = ServerAddress.FromUri(uri1);

        string str2 = serverAddress1.ToString();

        Assert.That(serverAddress1, Is.EqualTo(ServerAddress.FromUri(new Uri(str2))));
    }

    /// <summary>Verifies that the properties of a default constructed server address have the expected default values.
    /// </summary>
    [Test]
    public void ServerAddress_default_values()
    {
        var serverAddress = new ServerAddress();

        Assert.That(serverAddress.Protocol, Is.EqualTo(Protocol.IceRpc));
        Assert.That(serverAddress.Host, Is.EqualTo("::0"));
        Assert.That(serverAddress.Port, Is.EqualTo(Protocol.IceRpc.DefaultPort));
        Assert.That(serverAddress.Transport, Is.Null);
        Assert.That(serverAddress.Params, Has.Count.EqualTo(0));
    }

    /// <summary>Verifies that ServerAddress's constructor fails when a URI is not a valid server address.</summary>
    [TestCase("icerpc://host:10000/category/name")] // unexpected path
    [TestCase("icerpc://host:10000#fragment")] // unexpected fragment
    [TestCase("icerpc://host:10000?alt-server=host2")] // alt-server is service address only
    [TestCase("icerpc://host:10000?=bar")] // empty param name
    [TestCase("icerpc://host:10000?foo=bar")] // icerpc server address parameter
    [TestCase("icerpc:///foo")] // path, empty authority
    [TestCase("icerpc:///")] // empty authority
    [TestCase("icerpc://")] // empty authority
    [TestCase("icerpc:/foo")] // no authority
    [TestCase("icerpc:")] // no authority
    [TestCase("foo://host:10000")] // protocol not supported
    [TestCase("icerpc://user:password@host:10000")] // bad user-info
    public void Cannot_create_server_address_from_non_server_address_uri(Uri uri) =>
        Assert.Catch<ArgumentException>(() => ServerAddress.FromUri(uri));

    /// <summary>Verifies that a server address can be created from a URI.</summary>
    /// <param name="uri">The server address URI.</param>
    /// <param name="host">The expected host for the new server address.</param>
    /// <param name="port">The expected port for the new server address.</param>
    /// <param name="transport">The expected transport for the new server address.</param>
    /// <param name="parameters">The expected parameters for the new server address.</param>
    [Test]
    [TestCaseSource(nameof(ServerAddressUriSource))]
    public void Create_server_address_from_valid_uri(
        Uri uri,
        string host,
        ushort port,
        string? transport,
        IDictionary<string, string> parameters)
    {
        var serverAddress = ServerAddress.FromUri(uri);

        Assert.That(serverAddress.Host, Is.EqualTo(host));
        Assert.That(serverAddress.Port, Is.EqualTo(port));
        Assert.That(serverAddress.Transport, Is.EqualTo(transport));
        Assert.That(serverAddress.Params, Is.EquivalentTo(parameters));
    }

    /// <summary>Verifies that the variant of a server address created from a URI matches the URI scheme.</summary>
    [TestCase("ice://host", true)]
    [TestCase("icerpc://host", false)]
    public void Server_address_variant_matches_uri_scheme(Uri uri, bool isIce)
    {
        var serverAddress = ServerAddress.FromUri(uri);

        Assert.That(serverAddress is ServerAddress.Ice, Is.EqualTo(isIce));
        Assert.That(serverAddress is ServerAddress.IceRpc, Is.EqualTo(!isIce));
    }

    /// <summary>Verifies that the constructor of a variant rejects a URI with the scheme of the other variant.
    /// </summary>
    [Test]
    public void Variant_constructor_rejects_uri_with_other_scheme()
    {
        Assert.That(() => new ServerAddress.Ice(new Uri("icerpc://host")), Throws.ArgumentException);
        Assert.That(() => new ServerAddress.IceRpc(new Uri("ice://host")), Throws.ArgumentException);
    }

    /// <summary>Verifies that setting the host works with a supported host name, and that an IPv6 address specified
    /// with brackets is stored without them.</summary>
    /// <param name="host">The value to set the <see cref="ServerAddress.Host" /> property to.</param>
    /// <param name="expectedHost">The expected value of the <see cref="ServerAddress.Host" /> property.</param>
    [TestCase("localhost", "localhost")]
    [TestCase("[::0]", "::0")]
    [TestCase("::1", "::1")]
    public void Setting_the_server_address_host(string host, string expectedHost)
    {
        var serverAddress = new ServerAddress.IceRpc(new Uri("icerpc://localhost"));

        serverAddress = serverAddress with { Host = host };

        Assert.That(serverAddress.Host, Is.EqualTo(expectedHost));
    }

    [Test]
    public void Construction_with_unsupported_protocol_fails()
    {
        // Arrange
        var uri = new Uri("http://foo");

        // Act / Assert
        Assert.Throws<ArgumentException>(() => ServerAddress.FromUri(uri));
    }

    [Test]
    public void Construction_with_relative_uri_fails()
    {
        // Arrange
        var relativeUri = new Uri("foo", UriKind.Relative);

        // Act / Assert
        Assert.Throws<ArgumentException>(() => ServerAddress.FromUri(relativeUri));
    }

    [Test]
    public void To_uri_round_trips_the_constructor_uri()
    {
        // Arrange
        var uri = new Uri("icerpc://bar:1234");
        var serverAddress = ServerAddress.FromUri(uri);

        // Act
        var result = serverAddress.ToUri();

        // Assert
        Assert.That(result, Is.EqualTo(uri));
    }

    [Test]
    public void To_uri_reflects_an_updated_property()
    {
        // Arrange
        var serverAddress = new ServerAddress.IceRpc(new Uri("icerpc://localhost"));
        serverAddress = serverAddress with { Host = "foo" };

        // Act
        var serverAddressUri = serverAddress.ToUri();

        // Assert
        Assert.That(serverAddressUri.Scheme, Is.EqualTo("icerpc"));
        Assert.That(serverAddressUri.Host, Is.EqualTo("foo"));
    }

    /// <summary>Verifies that setting the server address parameters works.</summary>
    /// <param name="name">The name of the server address parameter to set.</param>
    /// <param name="value">The value of the server address parameter to set.</param>
    [TestCase("name", "value")]
    [TestCase("name%23[]", "value%25[]@!")]
    public void Setting_the_server_address_params(string name, string value)
    {
        var serverAddress = new ServerAddress.Ice(new Uri("ice://localhost"));

        serverAddress = serverAddress with { Params = serverAddress.Params.Add(name, value) };

        Assert.That(serverAddress.Params.ContainsKey(name), Is.True);
        Assert.That(serverAddress.Params[name], Is.EqualTo(value));
    }

    /// <summary>Verifies that trying to set the <see cref="ServerAddress.Host" /> to an invalid value throws
    /// <see cref="ArgumentException" /> and the <see cref="ServerAddress.Host" /> property remains unchanged.</summary>
    /// <param name="host">The invalid value for <see cref="ServerAddress.Host" /> property.</param>
    [TestCase("")]
    [TestCase("::1.2")]
    public void Setting_invalid_server_address_host_fails(string host)
    {
        var serverAddress = new ServerAddress.IceRpc(new Uri("icerpc://localhost"));

        Assert.Throws<ArgumentException>(() => _ = serverAddress with { Host = host });

        Assert.That(serverAddress.Host, Is.EqualTo("localhost"));
    }

    /// <summary>Verifies that trying to add an invalid server address parameter throws <see cref="ArgumentException" />
    /// and the <see cref="ServerAddress.Params" /> property remains unchanged.</summary>
    /// <param name="name">The server address parameter name.</param>
    /// <param name="value">The server address parameter value.</param>
    [TestCase("alt-server", "x")]
    [TestCase("", "value")]
    [TestCase(" name", "value")]
    [TestCase("name", "valu#e")] // cSpell:disable-line
    [TestCase("name", "valu&e")] // cSpell:disable-line
    public void Setting_invalid_server_address_params_fails(string name, string value)
    {
        var serverAddress = new ServerAddress.Ice(new Uri("ice://localhost"));

        Assert.Throws<ArgumentException>(() => _ = serverAddress with { Params = serverAddress.Params.Add(name, value) });

        Assert.That(serverAddress.Params, Has.Count.EqualTo(0));
    }

    [TestCase("icerpc://127.0.0.1?transport=foo", "icerpc://127.0.0.1:4062?transport=foo")]
    public void Server_address_equal(ServerAddress lhs, ServerAddress rhs) => Assert.That(lhs, Is.EqualTo(rhs));

    [TestCase("icerpc://127.0.0.1", "icerpc://localhost")]
    [TestCase("icerpc://127.0.0.1", "ice://127.0.0.1")]
    public void Server_address_not_equal(ServerAddress lhs, ServerAddress rhs) => Assert.That(lhs, Is.Not.EqualTo(rhs));
}
