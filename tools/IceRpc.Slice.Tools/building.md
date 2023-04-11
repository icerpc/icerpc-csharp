# IceRPC Slice Tools

## Packaging

When the `SLICE_TOOLS_PATH` MSBuild property is set, the NuGet package includes the `slicec-cs` compiler for all
supported platforms instead of including the `slice-cs` compiler from the current source build. To create the package
with all supported compilers, you must ensure that the binaries are available in the directory specified by
`SLICE_TOOLS_PATH`. The expected layout for `SLICE_TOOLS_PATH` is `<os-name>-<os-arch>/<compiler-executable>`.

The supported `<os-name>-<os-arch>` combinations are:

- `linux-x64`: Linux x86_64
- `linux-arm64`: Linux ARM64
- `macos-x64`: macOS x86_64
- `macos-arm64`: macOS Apple silicon
- `windows-x64`: Windows x64

You can set up the packaging in [IceRpc.Slice.Tools.csproj](./src/IceRpc.Slice.Tools/IceRpc.Slice.Tools.csproj).
