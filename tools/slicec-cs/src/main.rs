// Copyright (c) ZeroC, Inc.

mod builders;
mod comments;
mod cs_attributes;
mod cs_compile;
mod cs_options;
mod cs_util;
mod decoding;
mod encoding;
mod generators;
mod member_util;
mod slicec_ext;

#[cfg(test)]
mod attribute_tests;

use crate::cs_options::CsOptions;
use clap::Parser;
use cs_compile::{cs_patcher, cs_validator};
use cs_options::SLICEC_CS;
use generators::generate_from_slice_file;
use slicec::diagnostics::{Diagnostic, Error};
use std::fs::File;
use std::io;
use std::io::prelude::*;
use std::path::Path;

pub fn main() {
    let mut cs_options = CsOptions::parse();
    cs_options.slice_options.defined_symbols.push(SLICEC_CS.to_owned());
    let slice_options = &cs_options.slice_options;

    let mut compilation_state = slicec::compile_from_options(slice_options, cs_patcher, cs_validator);

    if !compilation_state.diagnostic_reporter.has_errors() && !slice_options.dry_run {
        for slice_file in compilation_state.files.values().filter(|file| file.is_source) {
            let code = generate_from_slice_file(slice_file, &cs_options);

            let path = match &slice_options.output_dir {
                Some(output_dir) => Path::new(output_dir),
                _ => Path::new("."),
            }
            .join(format!("{}.cs", &slice_file.filename))
            .to_owned();

            // If the file already exists and its contents match the generated code, we don't re-write it.
            if matches!(std::fs::read(&path), Ok(file_bytes) if file_bytes == code.as_bytes()) {
                continue;
            }

            match write_file(&path, &code) {
                Ok(_) => (),
                Err(error) => {
                    Diagnostic::new(Error::IO {
                        action: "write",
                        path: path.display().to_string(),
                        error,
                    })
                    .report(&mut compilation_state.diagnostic_reporter);
                    continue;
                }
            }
        }
    }

    std::process::exit(compilation_state.into_exit_code())
}

fn write_file(path: &Path, contents: &str) -> Result<(), io::Error> {
    let mut file = File::create(path)?;
    file.write_all(contents.as_bytes())
}
