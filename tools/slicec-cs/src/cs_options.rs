// Copyright (c) ZeroC, Inc. All rights reserved.

use clap::Parser;
use slice::command_line::SliceOptions;

// Note: StructOpt uses the doc-comments of fields to populate the '--help' output of slicec-cs.
//       boolean flags automatically default to false, and strings automatically default to empty.

/// This struct is responsible for parsing the command line options specific to 'slicec-cs'.
/// The option parsing capabilities are generated on the struct by the `StructOpt` macro.
#[derive(Debug, Parser)]
#[clap(author, version, about, long_about=DESCRIPTION, rename_all = "kebab-case")]
pub struct CsOptions {
    // Import the options common to all slice compilers.
    #[clap(flatten)]
    pub slice_options: SliceOptions,
}

/// Short description of slicec-cs that is displayed in its help dialogue.
const DESCRIPTION: &str = "The slice compiler for C#.
                           Generates C# code from Slice files for use with IceRpc.";
