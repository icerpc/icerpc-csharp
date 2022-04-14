// Copyright (c) ZeroC, Inc. All rights reserved.

mod builders;
mod class_visitor;
mod code_block;
mod comments;
mod cs_options;
mod cs_util;
mod cs_validator;
mod decoding;
mod dispatch_visitor;
mod encoded_result;
mod encoding;
mod enum_visitor;
mod exception_visitor;
mod generated_code;
mod member_util;
mod module_visitor;
mod proxy_visitor;
mod slicec_ext;
mod struct_visitor;
mod trait_visitor;

use blake2::{Blake2b, Digest};
use class_visitor::ClassVisitor;
use cs_options::CsOptions;
use cs_validator::CsValidator;
use dispatch_visitor::DispatchVisitor;
use enum_visitor::EnumVisitor;
use exception_visitor::ExceptionVisitor;
use generated_code::GeneratedCode;
use module_visitor::ModuleVisitor;
use proxy_visitor::ProxyVisitor;
use slice::error::Error;
use slice::slice_file::SliceFile;
use std::fs::File;
use std::io;
use std::io::prelude::*;
use std::path::Path;
use struct_visitor::StructVisitor;
use structopt::StructOpt;
use trait_visitor::TraitVisitor;

use crate::code_block::CodeBlock;

pub fn main() {
    std::process::exit(match try_main() {
        Ok(()) => 0,
        Err(_) => 1,
    })
}

fn try_main() -> Result<(), Error> {
    let options = CsOptions::from_args();
    let slice_options = &options.slice_options;
    let slice_files = slice::parse_from_options(slice_options)?;

    let mut cs_validator = CsValidator;
    for slice_file in slice_files.values() {
        slice_file.visit_with(&mut cs_validator);
    }
    slice::handle_errors(slice_options.warn_as_error, &slice_files)?;

    if !slice_options.validate {
        for slice_file in slice_files.values().filter(|file| file.is_source) {
            // TODO: actually check for the error

            let mut generated_code = GeneratedCode::new();

            generated_code.code_blocks.push(preamble(slice_file));

            let mut struct_visitor = StructVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut struct_visitor);

            let mut proxy_visitor = ProxyVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut proxy_visitor);

            let mut dispatch_visitor = DispatchVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut dispatch_visitor);

            let mut exception_visitor = ExceptionVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut exception_visitor);

            let mut enum_visitor = EnumVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut enum_visitor);

            let mut class_visitor = ClassVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut class_visitor);

            let mut trait_visitor = TraitVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut trait_visitor);

            let mut module_visitor = ModuleVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut module_visitor);

            {
                let path = match &slice_options.output_dir {
                    Some(output_dir) => Path::new(output_dir),
                    _ => Path::new("."),
                }
                .join(format!("{}.cs", &slice_file.filename))
                .to_owned();

                // Move the generated code out of the generated_code struct and consolidate into a single string.
                let code_string = generated_code
                    .code_blocks
                    .into_iter()
                    .collect::<CodeBlock>()
                    .into_string();

                // If the file already exits and the hash of its contents match the generated code,
                // we don't need to write it.
                if file_is_up_to_date(&code_string, &path) {
                    continue;
                }

                match write_file(&path, &code_string) {
                    Ok(_) => (),
                    Err(err) => {
                        slice::report_error(
                            format!("failed to write to file {}: {}", &path.display(), err),
                            None,
                        );

                        continue;
                    }
                }
            }
        }
    }

    slice::handle_errors(true, &slice_files)
}

fn preamble(slice_file: &SliceFile) -> CodeBlock {
    format!(
        r#"// Copyright (c) ZeroC, Inc. All rights reserved.

// <auto-generated/>
// slicec-cs version: '{version}'
// Generated from file: '{file}.slice'

#nullable enable

#pragma warning disable 1591 // Missing XML Comment

using IceRpc.Slice;

[assembly:IceRpc.Slice.Slice("{file}.slice")]"#,
        version = env!("CARGO_PKG_VERSION"),
        file = slice_file.filename
    )
    .into()
}

/// Returns `true` if the contents of the given file are up to date.
fn file_is_up_to_date(generated_code: &str, path: &Path) -> bool {
    let generated_code_hash = Blake2b::new().chain(generated_code).finalize();

    if let Ok(mut file) = File::open(path) {
        let mut hasher = Blake2b::new();

        if io::copy(&mut file, &mut hasher).is_ok() {
            let file_hash = hasher.finalize();
            if generated_code_hash == file_hash {
                return true;
            }
        }
    }
    false
}

fn write_file(path: &Path, contents: &str) -> Result<(), io::Error> {
    let mut file = File::create(path)?;
    file.write_all(contents.as_bytes())
}
