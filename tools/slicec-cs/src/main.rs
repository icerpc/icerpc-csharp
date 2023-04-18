// Copyright (c) ZeroC, Inc.

mod attribute_patcher;
mod builders;
mod code_gen;
mod comments;
mod cs_attributes;
mod cs_options;
mod cs_util;
mod decoding;
mod encoded_result;
mod encoding;
mod generated_code;
mod member_util;
mod slicec_ext;
mod validators;
mod visitors;

use code_gen::{compile, generate_code};
use cs_options::CsOptions;
use slice::clap::Parser;
use slice::compilation_result::CompilationResult;
use slice::diagnostics::{Diagnostic, Error};
use std::fs::File;
use std::io;
use std::io::prelude::*;
use std::path::Path;

pub fn main() {
    let compilation_data = match try_main() {
        Ok(data) => data,
        Err(data) => data,
    };
    std::process::exit(compilation_data.into_exit_code());
}

fn try_main() -> CompilationResult {
    let options = CsOptions::parse();
    let slice_options = &options.slice_options;
    let mut compilation_data = compile(slice_options)?;

    if !slice_options.dry_run {
        for slice_file in compilation_data.files.values().filter(|file| file.is_source) {
            let code_string = generate_code(slice_file);

            let path = match &slice_options.output_dir {
                Some(output_dir) => Path::new(output_dir),
                _ => Path::new("."),
            }
            .join(format!("{}.cs", &slice_file.filename))
            .to_owned();

            // If the file already exists and its contents match the generated code, we don't re-write it.
            if matches!(std::fs::read(&path), Ok(file_bytes) if file_bytes == code_string.as_bytes()) {
                continue;
            }

            match write_file(&path, &code_string) {
                Ok(_) => (),
                Err(error) => {
                    Diagnostic::new(Error::IO {
                        action: "write",
                        path: path.display().to_string(),
                        error,
                    })
                    .report(&mut compilation_data.diagnostic_reporter);
                    continue;
                }
            }
        }
    }

    compilation_data.into()
}

fn write_file(path: &Path, contents: &str) -> Result<(), io::Error> {
    let mut file = File::create(path)?;
    file.write_all(contents.as_bytes())
}
