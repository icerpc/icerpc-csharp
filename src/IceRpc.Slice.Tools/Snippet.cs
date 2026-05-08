// Copyright (c) ZeroC, Inc.

using System.Text.Json.Serialization;

namespace IceRpc.Slice.Tools;

public class Snippet
{
    [JsonPropertyName("span")]
    public SourceSpan SourceSpan { get; set; } = new();

    public string Text { get; set; } = "";
}
