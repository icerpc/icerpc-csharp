// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Tools;

public class SourceSpan
{
    public Location Start { get; set; }
    public Location End { get; set; }
    public string File { get; set; } = "";
}
