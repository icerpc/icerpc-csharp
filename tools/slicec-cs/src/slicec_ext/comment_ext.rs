// Copyright (c) ZeroC, Inc.

use super::EntityExt;
use crate::comments::CommentTag;
use slicec::grammar::*;

pub trait CommentExt: Commentable {
    /// If this entity has a doc comment with an overview on it, this returns it with any links resolved to the
    /// appropriate C# tag. Otherwise this returns `None`.
    fn formatted_doc_comment_summary(&self) -> Option<String> {
        self.comment().and_then(|comment| {
            comment
                .overview
                .as_ref()
                .map(|overview| format_comment_message(&overview.message, &self.namespace()))
        })
    }

    /// Returns this entity's see doc comments, formatted as a list of C# seealso doc comment tags. Any links present
    /// in these are resolved to the appropriate C# tag. If no see doc comment is present on this entity, this returns
    /// an empty vector.
    fn formatted_doc_comment_seealso(&self) -> Vec<CommentTag> {
        let mut comments = Vec::new();
        if let Some(comment) = self.comment() {
            // Add a see-also comment tag for each '@see' tag in the comment.
            for see_tag in &comment.see {
                match see_tag.linked_entity() {
                    Ok(entity) => {
                        // We re-use `get_formatted_link` to correctly generate the link, then rip out the link.
                        let formatted_link = entity.get_formatted_link(&self.namespace());
                        // The formatted link is always of the form `<tag attribute="link" />`. We get the link from
                        // from this by splitting the string on '"' characters, and taking the 2nd element.
                        let link = formatted_link.split('"').nth(1).unwrap();
                        comments.push(CommentTag::with_tag_attribute("seealso", "cref", link, String::new()));
                    }
                    Err(identifier) => {
                        // If there was an error resolving the link, print the identifier without any formatting.
                        let name = &identifier.value;
                        comments.push(CommentTag::with_tag_attribute("seealso", "cref", name, String::new()));
                    }
                }
            }
        }
        comments
    }
}

impl<T: Commentable + ?Sized> CommentExt for T {}

pub fn format_comment_message(message: &Message, namespace: &str) -> String {
    // Iterate through the components of the message and append them into a string.
    // If the component is text, append it as is. If the component is a link, format it first, then append it.
    message.iter().fold(String::new(), |s, component| match &component {
        MessageComponent::Text(text) => s + &xml_escape(text),
        MessageComponent::Link(link_tag) => match link_tag.linked_entity() {
            // If the link is to a valid entity, run the link formatter. Otherwise just use the link's raw text.
            Ok(entity) => s + &entity.get_formatted_link(namespace),
            Err(identifier) => s + &identifier.value,
        },
    })
}

fn xml_escape(text: &str) -> String {
    text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace("'", "&apos;")
        .replace("\"", "&quot;")
}
