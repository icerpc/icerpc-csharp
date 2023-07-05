# The Slice compiler for C#

The Slice compiler for C#, `slicec-cs`, allows you to compile Slice definitions (in `.slice` files) into C# code (in
`.cs` files).

[Package][package] | [Building from source][building] | [Slice documentation][slice-documentation]

## Predefined symbols

The slicec-cs compiler predefines the following preprocessor symbols:

`SLICEC_CS`

## Options

|                                           |                                                                                            |
|-------------------------------------------|--------------------------------------------------------------------------------------------|
| `-R <REFERENCE>`                          | Add a directory or Slice file to the list of references                                    |
| `-D <SYMBOL>`                             | Define a preprocessor symbol                                                               |
| `-A, --allow <WARNING>`                   | This flag will set which warnings should be set to the allow level                         |
| `--dry-run`                               | Validate input files without generating code for them                                      |
| `-O, --output-dir <OUTPUT_DIR>`           | Set the output directory for the generated code. Defaults to the current working directory |
| `--diagnostic-format <DIAGNOSTIC_FORMAT>` | Set the output format for emitted errors [default: human] [possible values: human, json]   |
| `--disable-color`                         | Disable ANSI color codes in diagnostic output                                              |
| `-h, --help`                              | Print help                                                                                 |
| `-V, --version`                           | Print version                                                                              |

[package]:  https://www.nuget.org/packages/IceRpc.Slice.Tools
[building]: ../../BUILDING.md
[slice-documentation]: https://docs.testing.zeroc.com/slice
