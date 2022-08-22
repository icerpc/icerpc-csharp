// Copyright (c) ZeroC, Inc. All rights reserved.

use std::convert::TryInto;

use crate::slicec_ext::*;

use slice::ast::node::Node;
use slice::grammar::{DocComment, Entity};
use slice::parse_result::{ParsedData, ParserResult};

struct CommentPatcher {
    parsed_data: ParsedData,
    patched_comments: Vec<Option<DocComment>>,
}

pub fn patch_comments(parsed_data: ParsedData) -> ParserResult {
    let mut patcher = CommentPatcher {
        parsed_data,
        patched_comments: vec![],
    };

    patcher.compute_patched_comments();
    patcher.apply_patched_comments();

    patcher.parsed_data.into()
}

impl CommentPatcher {
    fn compute_patched_comments(&mut self) {
        for node in self.parsed_data.ast.as_slice() {
            let entity_opt: Option<&dyn Entity> = node.try_into().ok();

            if let Some(entity) = entity_opt {
                let patch = self.create_patch(entity);
                self.patched_comments.push(patch);
            }
        }
    }

    fn apply_patched_comments(&mut self) {
        unsafe {
            for node in self.parsed_data.ast.as_mut_slice() {
                let entity_opt: Option<&dyn Entity> = (&*node).try_into().ok();

                if entity_opt.is_some() {
                    let patch = self.patched_comments.remove(0);
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
                        Node::Trait(trait_ptr) => trait_ptr.borrow_mut().comment = patch,
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

    fn create_patch(&self, entity: &dyn Entity) -> Option<DocComment> {
        if let Some(mut comment) = entity.comment().cloned() {
            replace_selection(&mut comment.overview, "{@link ", "}", |s| {
                let identifier = match self
                    .parsed_data
                    .ast
                    .find_element_with_scope::<dyn Entity>(s, entity.module_scope())
                {
                    Ok(e) => e.escape_scoped_identifier(&entity.namespace()),
                    Err(_) => s.to_owned(), // TODO log a warning
                };

                format!("<see cref=\"{identifier}\"/>")
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
