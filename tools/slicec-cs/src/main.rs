// Copyright (c) ZeroC, Inc.

mod builders;
mod code_block;
mod code_gen_util;
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

use crate::cs_options::{CsOptions, RpcProvider};
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

    if !compilation_state.diagnostics.has_errors() && !slice_options.dry_run {
        for slice_file in compilation_state.files.iter().filter(|file| file.is_source) {
            let code = generate_from_slice_file(slice_file, false, &cs_options);
            write_code(
                &slice_file.filename,
                &slice_options.output_dir,
                &code,
                &mut compilation_state.diagnostics,
            );

            if cs_options.rpc_provider == RpcProvider::IceRpc {
                let interface_code = generate_from_slice_file(slice_file, true, &cs_options);
                write_code(
                    &format!("{}.IceRpc", &slice_file.filename),
                    &slice_options.output_dir,
                    &interface_code,
                    &mut compilation_state.diagnostics,
                );
            }
        }
    }

    std::process::exit(i32::from(compilation_state.emit_diagnostics(slice_options)))
}

fn write_file(path: &Path, contents: &str) -> Result<(), io::Error> {
    let mut file = File::create(path)?;
    file.write_all(contents.as_bytes())
}

fn write_code(
    filename: &str,
    output_dir: &Option<String>,
    code: &str,
    diagnostics: &mut slicec::diagnostics::Diagnostics,
) {
    let path = match &output_dir {
        Some(value) => Path::new(value),
        _ => Path::new("."),
    }
    .join(format!("{}.cs", &filename));

    // If the file already exists and its contents match the generated code, we don't re-write it.
    if !matches!(std::fs::read(&path), Ok(file_bytes) if file_bytes == code.as_bytes()) {
        match write_file(&path, code) {
            Ok(_) => (),
            Err(error) => {
                Diagnostic::new(Error::IO {
                    action: "write",
                    path: path.display().to_string(),
                    error,
                })
                .push_into(diagnostics);
            }
        }
    }
}
