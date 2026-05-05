// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Tools;

public class Snippet
{
    [JsonPropertyName("span")]
    public SourceSpan SourceSpan { get; set; } = null;

    public string Text { get; set; } = "";
}
