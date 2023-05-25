// Copyright (c) ZeroC, Inc.

using System.Text.Json.Serialization;

namespace IceRpc.Slice.Tools;

public class Note
{
    public string Message { get; set; } = "";
    [JsonPropertyName("span")]
    public SourceSpan? SourceSpan { get; set; } = null;
}
