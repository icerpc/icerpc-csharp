// Copyright (c) ZeroC, Inc.

use super::generated_code::GeneratedCode;
use crate::builders::{Builder, ContainerBuilder};
use crate::cs_attributes::match_cs_namespace;
use slice::code_block::CodeBlock;

use slice::grammar::*;
use slice::slice_file::SliceFile;

pub fn generate_namespaces(slice_file: &SliceFile, generated_code: &mut GeneratedCode) {
    let top_level_modules = slice_file
        .contents
        .iter()
        .map(|module_ptr| module_ptr.borrow())
        .collect::<Vec<_>>();

    for module in top_level_modules {
        let code_block = module_code_block(module, None, generated_code);
        generated_code.code_blocks.push(code_block);
    }
}

fn module_code_block(module: &Module, module_prefix: Option<String>, generated_code: &mut GeneratedCode) -> CodeBlock {
    let code_blocks = generated_code.remove_scoped(module);

    let identifier = match module.find_attribute(false, match_cs_namespace) {
        Some(namespace) => namespace,
        _ => module.identifier().to_owned(),
    };

    let module_identifier = match &module_prefix {
        Some(prefix) => {
            // If there is a prefix the previous module was empty and we keep the prefix in the
            // C# namespace declaration as in `module Foo::Bar` -> `namespace Foo.Bar`
            format!("{prefix}.{identifier}")
        }
        None => identifier,
    };

    // If this module has any code blocks the submodules are mapped to namespaces inside the
    // current namespace (not using a prefix), otherwise if the current module doesn't
    // contain any code blocks we map the submodules with this module as a prefix.
    let submodules_code: CodeBlock = module
        .submodules()
        .iter()
        .map(|s| {
            module_code_block(
                s,
                if code_blocks.is_some() {
                    None
                } else {
                    Some(module_identifier.to_owned())
                },
                generated_code,
            )
        })
        .collect();

    if let Some(vec) = code_blocks {
        // Generate file-scoped namespace declaration if
        // - no other code blocks have been generated from another module
        // - all scoped code blocks have been consumed (can be no other modules or sub-modules)
        // - no submodules code for this module
        // - we are in a top-level module or,
        // - a submodule who's parent has no generated code code (ie. module_prefix.is_some())
        if generated_code.code_blocks.is_empty()
            && generated_code.scoped_code_blocks.is_empty()
            && submodules_code.is_empty()
            && (module_prefix.is_some() || module.is_top_level())
        {
            let mut code_block = CodeBlock::default();
            writeln!(code_block, "namespace {module_identifier};");

            for code in vec {
                code_block.add_block(&code);
            }

            code_block
        } else {
            let mut builder = ContainerBuilder::new("namespace", &module_identifier);

            for code in vec {
                builder.add_block(code);
            }

            builder.add_block(submodules_code);
            builder.build()
        }
    } else {
        submodules_code
    }
}
