// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::command_line::SliceOptions;
use structopt::StructOpt;

// Note: StructOpt uses the doc-comments of fields to populate the '--help' output of slicec-cs.
//       boolean flags automatically default to false, and strings automatically default to empty.

/// This struct is responsible for parsing the command line options specific to 'slicec-cs'.
/// The option parsing capabilities are generated on the struct by the `StructOpt` macro.
#[derive(StructOpt, Debug)]
#[structopt(name = "slicec-cs", version = "0.1.0", rename_all = "kebab-case", about = DESCRIPTION)]
pub struct CsOptions {
    // Import the options common to all slice compilers.
    #[structopt(flatten)]
    pub slice_options: SliceOptions,
}

/// Short description of slicec-cs that is displayed in its help dialogue.
const DESCRIPTION: &str = "The slice compiler for C#.
                           Generates C# code from Slice files for use with IceRpc.";
