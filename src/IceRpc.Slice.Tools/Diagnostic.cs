// Copyright (c) ZeroC, Inc.

using System.Text.Json.Serialization;
using System;

namespace IceRpc.Slice.Tools;

public class Diagnostic
{
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "";

    [JsonPropertyName("span")]
    public SourceSpan? SourceSpan { get; set; } = null;
    public Note[] Notes { get; set; } = Array.Empty<Note>();

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = "";
}
