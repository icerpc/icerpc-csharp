# The Slice compiler for C#

The Slice compiler for C#, `slicec-cs`, allows you to compile Slice definitions (in `.slice` files) into C# code (in
`.cs` files).

[Package][package] | [Building from source][building] | [Slice documentation][slice-documentation]

## Predefined symbols

slicec-cs implicitly defines the `SLICEC_CS` preprocessor symbol.

## Options

|                                |                                                                                                                    |
|--------------------------------|--------------------------------------------------------------------------------------------------------------------|
| `--rpc <RPC_PROVIDER>`         | Specify which RPC framework the generated code will be used with [default: icerpc] [possible values: none, icerpc] |
| `-R <REFERENCE>`               | Add a directory or Slice file to the list of references                                                            |
| `-D <SYMBOL>`                  | Define a preprocessor symbol                                                                                       |
| `-A, --allow <LINT_NAME>`      | Instruct the compiler to allow the specified lint                                                                  |
| `--dry-run`                    | Validate input files without generating code for them                                                              |
| `-O, --output-dir <DIRECTORY>` | Set the output directory for the generated code. Defaults to the current working directory                         |
| `--diagnostic-format <FORMAT>` | Specify how the compiler should emit errors and warnings [default: human] [possible values: human, json]           |
| `--disable-color`              | Disable ANSI color codes in diagnostic output                                                                      |
| `-h, --help`                   | Print help                                                                                                         |
| `-V, --version`                | Print version                                                                                                      |

[package]:  https://www.nuget.org/packages/IceRpc.Slice.Tools
[building]: ../../BUILDING.md
[slice-documentation]: https://docs.testing.zeroc.com/slice
