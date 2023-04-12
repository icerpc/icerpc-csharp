# IceRPC Slice Tools

## Packaging

When the `SLICEC_CS_STAGING_PATH` MSBuild property is set, the NuGet package includes the `slicec-cs` compiler for all
supported platforms instead of including the `slice-cs` compiler from the current source build. To create the package
with all supported compilers, you must ensure that the binaries for all the supported compilers are available in the
directory specified by `SLICEC_CS_STAGING_PATH` or the packaging task will fail with an error. The expected layout for
`SLICEC_CS_STAGING_PATH ` is `<os-name>-<os-arch>/<compiler-executable>`.

The supported `<os-name>-<os-arch>` combinations are:

- `linux-x64`: Linux x86_64
- `linux-arm64`: Linux ARM64
- `macos-x64`: macOS x86_64
- `macos-arm64`: macOS Apple silicon
- `windows-x64`: Windows x64

The NuGet package settings are defined in [IceRpc.Slice.Tools.csproj](./src/IceRpc.Slice.Tools/IceRpc.Slice.Tools.csproj)
project file.
