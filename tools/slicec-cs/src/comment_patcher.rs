// Copyright (c) ZeroC, Inc.

use std::collections::HashMap;

use crate::slicec_ext::*;

use slice::ast::node::Node;
use slice::ast::Ast;
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::grammar::{DocComment, Entity};

struct CommentPatcher {
    patched_comments: HashMap<usize, Option<DocComment>>,
}

pub fn patch_comments(mut compilation_data: CompilationData) -> CompilationResult {
    let mut patcher = CommentPatcher {
        patched_comments: HashMap::new(),
    };

    patcher.compute_patched_comments(&compilation_data.ast);
    patcher.apply_patched_comments(&mut compilation_data.ast);

    debug_assert!(patcher.patched_comments.is_empty());

    compilation_data.into()
}

impl CommentPatcher {
    fn compute_patched_comments(&mut self, ast: &Ast) {
        for (i, node) in ast.as_slice().iter().enumerate() {
            if let Ok(entity) = node.try_into() {
                let patch = self.patch_comment(ast, entity);
                self.patched_comments.insert(i, patch);
            }
        }
    }

    fn apply_patched_comments(&mut self, ast: &mut Ast) {
        unsafe {
            for (i, node) in ast.as_mut_slice().iter_mut().enumerate() {
                if let Some(patch) = self.patched_comments.remove(&i) {
                    match node {
                        Node::Module(module_ptr) => module_ptr.borrow_mut().comment = patch,
                        Node::Struct(struct_ptr) => struct_ptr.borrow_mut().comment = patch,
                        Node::Class(class_ptr) => class_ptr.borrow_mut().comment = patch,
                        Node::Exception(exception_ptr) => exception_ptr.borrow_mut().comment = patch,
                        Node::DataMember(data_member_ptr) => data_member_ptr.borrow_mut().comment = patch,
                        Node::Interface(interface_ptr) => interface_ptr.borrow_mut().comment = patch,
                        Node::Operation(operation_ptr) => operation_ptr.borrow_mut().comment = patch,
                        Node::Parameter(parameter_ptr) => parameter_ptr.borrow_mut().comment = patch,
                        Node::Enum(enum_ptr) => enum_ptr.borrow_mut().comment = patch,
                        Node::Enumerator(enumerator_ptr) => enumerator_ptr.borrow_mut().comment = patch,
                        Node::CustomType(custom_type_ptr) => custom_type_ptr.borrow_mut().comment = patch,
                        Node::TypeAlias(type_alias_ptr) => type_alias_ptr.borrow_mut().comment = patch,
                        _ => {
                            panic!("Unexpected node type");
                        }
                    }
                }
            }
        }
    }

    fn patch_comment(&self, ast: &Ast, entity: &dyn Entity) -> Option<DocComment> {
        if let Some(mut comment) = entity.comment().cloned() {
            replace_selection(&mut comment.overview, "{@link ", "}", |s| {
                let identifier = match ast.find_element_with_scope::<dyn Entity>(s, entity.module_scope()) {
                    Ok(e) => e.escape_scoped_identifier(&entity.namespace()),
                    // slicec verifies that the link is valid and issues a warning if
                    // the entity can't be found. In this case we just use the original
                    // string.
                    Err(_) => s.to_owned(),
                };

                format!("<see cref=\"{identifier}\" />")
            });

            Some(comment)
        } else {
            None
        }
    }
}

fn replace_selection<F>(s: &mut String, starts_with: &str, ends_with: &str, func: F)
where
    F: Fn(&str) -> String,
{
    while let Some(pos) = s.find(starts_with) {
        if let Some(end) = s[pos..].find(ends_with) {
            let replacement = func(s[pos + starts_with.len()..end + pos].trim());
            s.replace_range(pos..pos + end + 1, &replacement);
        } else {
            break;
        }
    }
}
