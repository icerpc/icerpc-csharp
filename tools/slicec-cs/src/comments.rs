// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::{Commentable, DocComment, Operation};
use std::fmt;

use regex::Regex;

#[derive(Clone, Debug)]
pub struct CommentTag {
    tag: String,
    content: String,
    attribute_name: Option<String>,
    attribute_value: Option<String>,
}

impl CommentTag {
    pub fn new(tag: &str, content: &str) -> Self {
        Self {
            tag: tag.to_owned(),
            content: content.to_owned(),
            attribute_name: None,
            attribute_value: None,
        }
    }

    pub fn with_tag_attribute(
        tag: &str,
        attribute_name: &str,
        attribute_value: &str,
        content: &str,
    ) -> Self {
        Self {
            tag: tag.to_owned(),
            content: content.to_owned(),
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
            (Some(name), Some(value)) => format!(r#" {}="{}""#, name, value),
            _ => "".to_owned(),
        };

        write!(
            f,
            "/// <{tag}{attribute}>{content}</{tag}>",
            tag = self.tag,
            attribute = attribute,
            content = self
                .content
                .trim_matches(char::is_whitespace)
                .replace("\n", "\n/// ")
        )
    }
}

// TODO this should probably be converted into an extension trait.
pub struct CsharpComment(pub DocComment);

impl CsharpComment {
    pub fn new(comment: &DocComment) -> Self {
        // process comment here
        // replace @link @see, etc.
        let mut comment = comment.clone();

        // Replace comments like '<code>my code</code>' by 'my code'
        let re: regex::Regex = Regex::new(r"(?ms)<.+>\s?(?P<content>.+)\s?</.+>").unwrap();
        comment.overview = re.replace_all(&comment.overview, "${content}").to_string();

        // Replace comments like '{@link FooBar}' by 'FooBar'
        let re: regex::Regex = Regex::new(r"\{@link\s+(?P<link>\w+)\s?\}").unwrap();
        comment.overview = re.replace_all(&comment.overview, "${link}").to_string();

        // TODO: ${see} should actually be replaced by the real Csharp identifier (see
        // csharpIdentifier in C++)
        let re: regex::Regex = Regex::new(r"\{@see\s+(?P<see>\w+)\s?\}").unwrap();
        comment.overview = re
            .replace_all(&comment.overview, r#"<see cref="${see}"/>"#)
            .to_string();

        CsharpComment(comment)
    }
}

impl fmt::Display for CsharpComment {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let comment = &self.0;

        // Write the comment's summary message.
        writeln!(f, "{}", CommentTag::new("summary", &comment.overview))?;

        // Write deprecate reason if present
        if let Some(reason) = &comment.deprecate_reason {
            writeln!(f, "{}", CommentTag::new("para", reason))?;
        }

        // Write each of the comment's parameter fields.
        for param in &comment.params {
            let (identifier, description) = param;
            writeln!(
                f,
                "{}",
                CommentTag::with_tag_attribute("param", "name", identifier, description)
            )?;
        }

        // Write the comment's returns message if it has one.
        if let Some(returns) = &comment.returns {
            writeln!(f, "{}", CommentTag::new("returns", returns))?;
        }

        // Write each of the comment's exception fields.
        for exception in &comment.throws {
            let (exception, description) = exception;
            writeln!(
                f,
                "{}",
                CommentTag::with_tag_attribute("exception", "cref", exception, description)
            )?;
        }

        Ok(())
    }
}

pub fn doc_comment_message(entity: &dyn Commentable) -> String {
    entity
        .comment()
        .map_or_else(|| "".to_owned(), |c| CsharpComment::new(c).0.overview)
}

// TODO: the `DocComment` message for an operation parameter should be the same as the `DocComment`
// for the operation param
pub fn operation_parameter_doc_comment<'a>(
    operation: &'a Operation,
    parameter_name: &str,
) -> Option<&'a str> {
    operation.comment().and_then(|comment| {
        comment
            .params
            .iter()
            .find(|(param, _)| param == parameter_name)
            .map(|(_, description)| description.as_str())
    })
}
