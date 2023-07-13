// Copyright (c) ZeroC, Inc.

use clap::{Parser, ValueEnum};
use slicec::slice_options::SliceOptions;

/// `slicec-cs` automatically defines this preprocessor symbol when compiling.
pub const SLICEC_CS: &str = "SLICEC_CS";

// Note: Clap uses the doc-comments of fields to populate the '--help' output of slicec-cs.
//       boolean flags automatically default to false, and strings automatically default to empty.

/// This struct is responsible for parsing the command line options specific to 'slicec-cs'.
/// The option parsing capabilities are generated on the struct by the `clap` macro.
#[derive(Debug, Parser)]
#[command(author, version, about, long_about=DESCRIPTION, rename_all = "kebab-case")]
pub struct CsOptions {
    /// Generate code for the specified RPC framework.
    #[arg(long = "rpc", value_enum, default_value_t = RpcProvider::IceRpc, ignore_case = true)]
    pub rpc_provider: RpcProvider,

    // Import the options common to all slice compilers.
    #[command(flatten)]
    pub slice_options: SliceOptions,
}

impl Default for CsOptions {
    fn default() -> Self {
        let mut slice_options = SliceOptions::default();
        slice_options.defined_symbols.push(SLICEC_CS.to_owned());

        CsOptions {
            rpc_provider: RpcProvider::default(),
            slice_options,
        }
    }
}

/// Short description of slicec-cs that is displayed in its help dialogue.
const DESCRIPTION: &str = "\
The Slice compiler for C#.
Generates C# code from Slice files for use with IceRpc.";

/// This enum is used to specify which RPC framework (if any) the generated code will be used with.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq, ValueEnum)]
pub enum RpcProvider {
    /// No RPC framework is being used.
    /// With this set, the generated code is purely for encoding and decoding data.
    #[default]
    None,

    /// IceRPC is being used.
    /// With this set, a `using IceRpc.Slice` statement is added to the preamble of any generated code.
    #[clap(name = "icerpc")]
    IceRpc,
}
