use crate::code_block::CodeBlock;
use std::collections::HashMap;

use slice::grammar::{Module, NamedSymbol, ScopedSymbol};

#[derive(Debug)]
pub struct GeneratedCode {
    /// Generated "top-level" code blocks
    pub code_blocks: Vec<CodeBlock>,

    /// Private map of scoped module names to their code blocks.
    scoped_code_blocks: HashMap<String, Vec<CodeBlock>>,
}

impl GeneratedCode {
    pub fn new() -> GeneratedCode {
        GeneratedCode { scoped_code_blocks: HashMap::new(), code_blocks: Vec::new() }
    }

    pub fn insert_scoped(&mut self, symbol: &dyn ScopedSymbol, code: CodeBlock) {
        let module_scope = symbol.module_scope().to_owned();
        match self.scoped_code_blocks.get_mut(&module_scope) {
            Some(vec) => vec.push(code),
            None => {
                self.scoped_code_blocks.insert(module_scope, vec![code]);
            }
        };
    }

    /// Removes (and returns) the code blocks for the given module.
    pub fn remove_scoped(&mut self, module: &Module) -> Option<Vec<CodeBlock>> {
        self.scoped_code_blocks.remove(&module.module_scoped_identifier())
    }
}
