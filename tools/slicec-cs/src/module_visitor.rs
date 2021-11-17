// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::ContainerBuilder;
use crate::code_block::CodeBlock;
use crate::generated_code::GeneratedCode;
use slice::grammar::*;
use slice::slice_file::SliceFile;
use slice::visitor::Visitor;

pub struct ModuleVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl Visitor for ModuleVisitor<'_> {
    fn visit_file_end(&mut self, slice_file: &SliceFile) {
        let top_level_modules = slice_file
            .contents
            .iter()
            .map(|module_ptr| module_ptr.borrow())
            .collect::<Vec<_>>();

        for module in top_level_modules {
            let code_block = self.module_code_block(module, None);
            self.generated_code.code_blocks.push(code_block);
        }
    }
}

impl ModuleVisitor<'_> {
    fn module_code_block(&mut self, module: &Module, module_prefix: Option<String>) -> CodeBlock {
        let module_identifier = match &module_prefix {
            Some(prefix) => {
                // If there is a prefix the previous module was empty and we keep the prefix in the
                // C# namespace declaration as in `module Foo::Bar` -> `namespace Foo.Bar`
                format!("{}.{}", prefix, module.identifier())
            }
            None => match module.get_attribute("cs:namespace", false) {
                // If a top-level module has a 'cs:namespace' attribute, use its argument as module indetifier
                // otherwise use the module indentifier.
                Some(attribute) if module.is_top_level() => attribute.first().unwrap().to_owned(),
                _ => module.identifier().to_owned(),
            },
        };

        let code_blocks = self.generated_code.remove_scoped(module);
        // If this module has any code blocks the submodules are mapped to namespaces inside the current
        // namespace (not using a prefix), otherwise if the current module doesn't contain any code blocks
        // we map the submodules with this module as a prefix.
        let submodules_code: CodeBlock = module
            .submodules()
            .iter()
            .map(|s| {
                self.module_code_block(
                    s,
                    if code_blocks.is_some() {
                        None
                    } else {
                        Some(module_identifier.to_owned())
                    },
                )
            })
            .collect();

        if let Some(vec) = code_blocks {
            let mut builder = ContainerBuilder::new("namespace", &module_identifier);

            for code in vec {
                builder.add_block(code);
            }

            builder.add_block(submodules_code);
            builder.build().into()
        } else {
            submodules_code
        }
    }
}
