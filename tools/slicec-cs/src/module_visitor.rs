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
    fn visit_file_start(&mut self, slice_file: &SliceFile) {
        let mut top_level_modules = slice_file
            .contents
            .iter()
            .map(|module_ptr| module_ptr.borrow())
            .collect::<Vec<_>>();

        // Must be sorted first for dedup to work
        top_level_modules.sort_by_key(|module| module.identifier());
        top_level_modules.dedup_by_key(|module| module.identifier());

        for module in top_level_modules {
            let code_block = self.module_code_block(module, None);
            self.generated_code.code_blocks.push(code_block);
        }
    }
}

impl ModuleVisitor<'_> {
    fn module_code_block(&mut self, module: &Module, module_prefix: Option<String>) -> CodeBlock {
        let submodules = module.submodules();
        let code_blocks = self.generated_code.remove_scoped(module);

        let module_identifier = match &module_prefix {
            Some(prefix) => format!("{}.{}", prefix, module.identifier()),
            None => match module.get_attribute("cs:namespace", false) {
                Some(attribute) if module.is_top_level() => attribute.first().unwrap().to_owned(),
                _ => module.identifier().to_owned(),
            },
        };

        let submodule_prefix = match &module_prefix {
            Some(_) if code_blocks.is_some() => None,
            _ => Some(module_identifier.to_owned()),
        };

        let submodules_code: CodeBlock = submodules
            .iter()
            .map(|s| self.module_code_block(s, submodule_prefix.to_owned()))
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
