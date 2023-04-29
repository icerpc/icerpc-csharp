// Copyright (c) ZeroC, Inc.

using System.Collections;

namespace IceRpc.Features;

/// <summary>Default implementation for <see cref="IRequestContextFeature" />.</summary>
/// <remarks>This class implements <see cref="IEnumerable" />, <see cref="Add" /> and the indexer operator to allow you
/// to use a collection initializer.</remarks>
public sealed class RequestContextFeature : IRequestContextFeature, IEnumerable<KeyValuePair<string, string>>
{
    /// <inheritdoc/>
    public IDictionary<string, string> Value { get; }

    /// <summary>Gets or sets an entry in <see cref="Value" />.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>>
    public string this[string key]
    {
        get => Value[key];
        set => Value[key] = value;
    }

    /// <summary>Constructs an empty writeable request context feature.</summary>
    public RequestContextFeature() => Value = new Dictionary<string, string>();

    /// <summary>Constructs a request context feature with the specified dictionary.</summary>
    /// <param name="dictionary">The dictionary that the new feature will hold.</param>
    public RequestContextFeature(IDictionary<string, string> dictionary) => Value = dictionary;

    /// <summary>Adds a new entry to <see cref="Value" />.</summary>
    /// <param name="key">The key of the new entry.</param>
    /// <param name="value">The value of the new entry.</param>
    public void Add(string key, string value) => Value.Add(key, value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Value.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Value).GetEnumerator();
}
