// Copyright (c) ZeroC, Inc.

use std::{char, fmt};

#[derive(Clone, Debug)]
pub struct CommentTag {
    pub tag: String,
    pub content: String,
    pub attribute_name: Option<String>,
    pub attribute_value: Option<String>,
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
        let attribute = match (&self.attribute_name, &self.attribute_value) {
            (Some(name), Some(value)) => format!(r#" {name}="{value}""#),
            _ => "".to_owned(),
        };

        let content = self.content.trim_matches(char::is_whitespace).replace('\n', "\n/// ");
        if content.is_empty() {
            write!(f, "/// <{tag}{attribute} />", tag = self.tag)
        } else {
            write!(f, "/// <{tag}{attribute}>{content}</{tag}>", tag = self.tag)
        }
    }
}
