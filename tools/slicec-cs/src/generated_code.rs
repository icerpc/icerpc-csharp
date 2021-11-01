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
        let scope = symbol.scope().to_owned();
        match self.scoped_code_blocks.get_mut(&scope) {
            Some(vec) => vec.push(code),
            None => {
                self.scoped_code_blocks.insert(scope, vec![code]);
            }
        };
    }

    /// Removes (and returns) the code blocks for the given module.
    pub fn remove_scoped(&mut self, module: &Module) -> Option<Vec<CodeBlock>> {
        let scope = format!(
            "{}::{}",
            if module.is_top_level() { "" } else { module.scope() },
            module.identifier()
        );
        self.scoped_code_blocks.remove(&scope)
    }
}
