// Copyright (c) ZeroC, Inc.

use clap::Parser;
use slice::slice_options::SliceOptions;

// Note: Clap uses the doc-comments of fields to populate the '--help' output of slicec-cs.
//       boolean flags automatically default to false, and strings automatically default to empty.

/// This struct is responsible for parsing the command line options specific to 'slicec-cs'.
/// The option parsing capabilities are generated on the struct by the `clap` macro.
#[derive(Debug, Default, Parser)]
#[command(author, version, about, long_about=DESCRIPTION, rename_all = "kebab-case")]
pub struct CsOptions {
    // Import the options common to all slice compilers.
    #[command(flatten)]
    pub slice_options: SliceOptions,
}

/// Short description of slicec-cs that is displayed in its help dialogue.
const DESCRIPTION: &str = "\
The slice compiler for C#.
Generates C# code from Slice files for use with IceRpc.";
