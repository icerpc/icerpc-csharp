// Copyright (c) ZeroC, Inc.

using System.Text;

namespace IceRpc.Slice.BuildTelemetry;

internal partial record struct TelemetryData
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Architecture = ").Append(Architecture);
        builder.Append(", OperatingSystem = ").Append(OperatingSystem);
        builder.Append(", SlicecVersion = ").Append(SlicecVersion);
        builder.Append(", Toolchain = ");
        if (Toolchain is ToolchainInfo toolchain)
        {
            builder.Append(toolchain.Name).Append(' ').Append(toolchain.Version);
        }
        else
        {
            builder.Append("(none)");
        }
        builder.Append(", DryRun = ").Append(DryRun);
        builder.Append(", CI = ").Append(Ci);
        builder.Append(", Generators = [");
        for (int i = 0; i < Generators.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }
            GeneratorInfo generator = Generators[i];
            builder.Append(generator.Name).Append(' ').Append(generator.Version);
        }
        builder.Append(']');
        builder.Append(", InterfaceCount = ").Append(InterfaceCount);
        builder.Append(", OperationCount = ").Append(OperationCount);
        builder.Append(", TypeCount = ").Append(TypeCount);
        return true;
    }
}
