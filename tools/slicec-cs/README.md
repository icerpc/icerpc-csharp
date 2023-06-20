# The Slice compiler for C#

The Slice compiler for C#, `slicec-cs`, allows you to compile Slice definitions (in `.slice` files) into C# code (in
`.cs` files).

[Package][package] | [Building from source][building] | [Slice documentation][slice-documentation]

## Options

|                                           |                                                                                            |
|-------------------------------------------|--------------------------------------------------------------------------------------------|
| `-R <REFERENCE>`                          | Add a directory or Slice file to the list of references                                    |
| `-D <DEFINITION>`                         | Define a preprocessor definition                                                           |
| `-W, --warn-as-error`                     | Instruct the compiler to treat warnings as errors                                          |
| `-A, --allow <WARNING>`                   | Instruct the compiler to allow (not emit) the specified warning                            |
| `--dry-run`                               | Validate input files without generating code for them                                      |
| `-O, --output-dir <OUTPUT_DIR>`           | Set the output directory for the generated code. Defaults to the current working directory |
| `--diagnostic-format <DIAGNOSTIC_FORMAT>` | Set the output format for emitted errors [default: human] [possible values: human, json]   |
| `--disable-color`                         | Disable ANSI color codes in diagnostic output                                              |
| `-h, --help`                              | Print help (see more with '--help')                                                        |
| `-V, --version`                           | Print version                                                                              |

[package]:  https://www.nuget.org/packages/IceRpc.Slice.Tools
[building]: ../../BUILDING.md
[slice-documentation]: https://docs.testing.zeroc.com/docs/slice
