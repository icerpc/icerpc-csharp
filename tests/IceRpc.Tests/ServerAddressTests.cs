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
            (new Uri("icerpc://[::0]?xyz=false&xyz=true&foo=&b="),
             "::",
             4062,
             null,
             new Dictionary<string, string>() { ["xyz"] = "false,true", ["foo"] = "", ["b"] = "" }),
            (new Uri("icerpc://host:10000?xyz=foo"),
             "host",
             10000,
             null,
             new Dictionary<string, string> { ["xyz"] = "foo" }),
            (new Uri("icerpc://host:10000?transport=coloc"), "host", 10000, "coloc", null),
            (new Uri("ice://localhost?transport=tcp"), "localhost", 4061, "tcp", null),
            (new Uri("ice://host:10000"), "host", 10000, null, null),
            (new Uri("icerpc://host:10000?xyz"),
             "host",
             10000,
             null,
             new Dictionary<string, string>{ ["xyz"] = "" }),
            (new Uri("icerpc://host:10000?xyz&adapter-id=ok"),
             "host",
             10000,
             null,
             new Dictionary<string, string> { ["xyz"] = "", ["adapter-id"] = "ok" }),
            (new Uri("IceRpc://host:10000"), "host", 10000, null, null),
            // parses ok even though not a valid name
            (new Uri("icerpc://host:10000? =bar"),
             "host",
             10000,
             null,
             new Dictionary<string, string>() { ["%20"] = "bar" })
        };

    /// <summary>Verifies that a server address can be correctly converted into a string.</summary>
    /// <param name="uri1">The server address URI to test.</param>
    [Test, TestCaseSource(nameof(ServerAddressToStringSource))]
    public void Convert_an_server_address_into_a_string(Uri uri1)
    {
        var serverAddress1 = new ServerAddress(uri1);

        string str2 = serverAddress1.ToString();

        Assert.That(serverAddress1, Is.EqualTo(new ServerAddress(new Uri(str2))));
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

    /// <summary>Verifies that the <see cref="ServerAddress.OriginalUri" /> property is set for a server address created
    /// from an URI.</summary>
    [Test]
    public void ServerAddress_original_URI()
    {
        var serverAddress = new ServerAddress(new Uri("icerpc://host:10000?transport=foobar"));

        Assert.That(serverAddress.OriginalUri, Is.Not.Null);
    }

    /// <summary>Verifies that the <see cref="ServerAddress.OriginalUri" /> property of a server address is set to null
    /// after modifying the server address.</summary>
    [Test]
    public void ServerAddress_original_URI_is_null_after_updating_the_server_address()
    {
        var serverAddress = new ServerAddress(new Uri("icerpc://host:10000?transport=foobar"));

        ServerAddress serverAddress2 = serverAddress with { Host = "localhost", Port = 10001 };

        Assert.That(serverAddress.OriginalUri, Is.Not.Null);
        Assert.That(serverAddress2.OriginalUri, Is.Null);
    }

    /// <summary>Verifies that ServerAddress's constructor fails when a URI is not a valid server address.</summary>
    [TestCase("icerpc://host:10000/category/name")]                // unexpected path
    [TestCase("icerpc://host:10000#fragment")]                     // unexpected fragment
    [TestCase("icerpc://host:10000?alt-server=host2")]           // alt-server is service address only
    [TestCase("icerpc://host:10000?=bar")]                         // empty param name
    [TestCase("icerpc:///foo")]                                    // path, empty authority
    [TestCase("icerpc:///")]                                       // empty authority
    [TestCase("icerpc://")]                                        // empty authority
    [TestCase("icerpc:/foo")]                                      // no authority
    [TestCase("icerpc:")]                                          // no authority
    [TestCase("foo://host:10000")]                                 // protocol not supported
    [TestCase("icerpc://user:password@host:10000")]                // bad user-info
    public void Cannot_create_server_address_from_non_server_address_uri(Uri uri) =>
        Assert.Catch<ArgumentException>(() => new ServerAddress(uri));

    /// <summary>Verifies that a server address can be created from a URI.</summary>
    /// <param name="uri">The server address URI.</param>
    /// <param name="host">The expected host for the new server address.</param>
    /// <param name="port">The expected port for the new server address.</param>
    /// <param name="transport">The expected transport for the new server address.</param>
    /// <param name="parameters">The expected parameters for the new server address.</param>
    [Test, TestCaseSource(nameof(ServerAddressUriSource))]
    public void Create_server_address_from_valid_uri(
        Uri uri,
        string host,
        ushort port,
        string? transport,
        IDictionary<string, string> parameters)
    {
        var serverAddress = new ServerAddress(uri);

        Assert.That(serverAddress.Host, Is.EqualTo(host));
        Assert.That(serverAddress.Port, Is.EqualTo(port));
        Assert.That(serverAddress.Transport, Is.EqualTo(transport));
        Assert.That(serverAddress.Params, Is.EquivalentTo(parameters));
    }

    /// <summary>Verifies that setting the host works with a supported host name.</summary>
    /// <param name="host">The value to set the <see cref="ServerAddress.Host" /> property to.</param>
    [TestCase("localhost")]
    [TestCase("[::0]")]
    [TestCase("::1")]
    public void Setting_the_server_address_host(string host)
    {
        var serverAddress = new ServerAddress(new Uri("icerpc://localhost"));

        serverAddress = serverAddress with { Host = host };

        Assert.That(serverAddress.Host, Is.EqualTo(host));
    }

    [Test]
    public void Construction_with_unsupported_protocol_fails()
    {
        // Arrange
        var uri = new Uri("http://foo");

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new ServerAddress(uri));
    }

    [Test]
    public void Construction_with_relative_uri_fails()
    {
        // Arrange
        var relativeUri = new Uri("foo", UriKind.Relative);

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new ServerAddress(relativeUri));
    }

    [Test]
    public void To_Uri_returns_uri_from_ServerAddress_uri_constructor()
    {
        // Arrange
        var uri = new Uri("icerpc://bar:1234");
        var serverAddress = new ServerAddress(uri);

        // Act
        var result = serverAddress.ToUri();

        // Assert
        Assert.That(serverAddress.OriginalUri, Is.EqualTo(uri));
        Assert.That(result, Is.EqualTo(uri));
    }

    [Test]
    public void Original_uri_set_to_null_when_setting_property()
    {
        // Arrange
        var serverAddress = new ServerAddress(new Uri("icerpc://localhost"));
        serverAddress = serverAddress with { Host = "foo" }; // new host invalidates OriginalUri

        // Act
        var serverAddressUri = serverAddress.ToUri();

        // Assert
        Assert.That(serverAddressUri.Scheme, Is.EqualTo("icerpc"));
        Assert.That(serverAddressUri.Host, Is.EqualTo("foo"));
        Assert.That(serverAddress.OriginalUri, Is.Null);
    }

    /// <summary>Verifies that setting the server address parameters works.</summary>
    /// <param name="name">The name of the server address parameter to set.</param>
    /// <param name="value">The value of the server address parameter to set.</param>
    [TestCase("name", "value")]
    [TestCase("name%23[]", "value%25[]@!")]
    public void Setting_the_server_address_params(string name, string value)
    {
        var serverAddress = new ServerAddress(new Uri("icerpc://localhost"));

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
        var serverAddress = new ServerAddress(new Uri("icerpc://localhost"));

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
        var serverAddress = new ServerAddress(new Uri("icerpc://localhost"));

        Assert.Throws<ArgumentException>(() => _ = serverAddress with { Params = serverAddress.Params.Add(name, value) });

        Assert.That(serverAddress.Params, Has.Count.EqualTo(0));
    }
}
