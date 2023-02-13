// Copyright (c) ZeroC, Inc.

use std::{char, fmt};

use slice::grammar::{Commentable, Entity, Operation};

#[derive(Clone, Debug)]
pub struct CommentTag {
    tag: String,
    content: String,
    attribute_name: Option<String>,
    attribute_value: Option<String>,
}

impl CommentTag {
    pub fn new(tag: &str, content: String) -> Self {
        Self {
            tag: tag.to_owned(),
            content,
            attribute_name: None,
            attribute_value: None,
        }
    }

    pub fn with_tag_attribute(tag: &str, attribute_name: &str, attribute_value: &str, content: String) -> Self {
        Self {
            tag: tag.to_owned(),
            content,
            attribute_name: Some(attribute_name.to_owned()),
            attribute_value: Some(attribute_value.to_owned()),
        }
    }
}

impl fmt::Display for CommentTag {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        if self.content.is_empty() {
            // If the comment has no content don't write anything.
            return Ok(());
        }

        let attribute = match (&self.attribute_name, &self.attribute_value) {
            (Some(name), Some(value)) => format!(r#" {name}="{value}""#),
            _ => "".to_owned(),
        };

        write!(
            f,
            "/// <{tag}{attribute}>{content}</{tag}>",
            tag = self.tag,
            content = self.content.trim_matches(char::is_whitespace).replace('\n', "\n/// "),
        )
    }
}

pub fn doc_comment_message(entity: &dyn Entity) -> &str {
    entity.comment().map_or("", |comment| &comment.overview)
}

// TODO: the `DocComment` message for an operation parameter should be the same as the `DocComment`
// for the operation param
pub fn operation_parameter_doc_comment<'a>(operation: &'a Operation, parameter_name: &str) -> Option<&'a str> {
    operation.comment().and_then(|comment| {
        comment
            .params
            .iter()
            .find(|(param, _)| param == parameter_name)
            .map(|(_, description)| description.as_str())
    })
}
