// Copyright (c) ZeroC, Inc.

use super::{scoped_identifier, InterfaceExt, MemberExt};
use crate::comments::CommentTag;
use crate::cs_attributes::{match_cs_identifier, match_cs_internal, match_cs_namespace, match_cs_readonly};
use crate::cs_util::escape_keyword;
use slice::convert_case::{Case, Casing};
use slice::grammar::*;
use slice::utils::code_gen_util::format_message;

pub trait EntityExt: Entity {
    // Returns the  C# identifier for the entity, which is either the Slice identifier formatted with the specified
    // casing or the identifier specified by the cs::identifier attribute if present.
    fn cs_identifier(&self, case: Option<Case>) -> String {
        let identifier_attribute = self.attributes(false).into_iter().find_map(match_cs_identifier);

        match identifier_attribute {
            Some(identifier) => identifier,
            None => match case {
                Some(case) => self.identifier().to_case(case),
                None => self.identifier().to_string(),
            },
        }
    }

    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String {
        escape_keyword(&self.cs_identifier(Some(Case::Pascal)))
    }

    /// Appends the provided prefix to the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_prefix(&self, prefix: &str) -> String {
        escape_keyword(&format!("{}{}", prefix, self.cs_identifier(Some(Case::Pascal))))
    }

    /// Concatenates the provided suffix on the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_suffix(&self, suffix: &str) -> String {
        escape_keyword(&format!("{}{}", self.cs_identifier(Some(Case::Pascal)), suffix))
    }

    /// Applies the provided prefix and suffix to the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_prefix_and_suffix(&self, prefix: &str, suffix: &str) -> String {
        escape_keyword(&format!("{prefix}{}{suffix}", self.cs_identifier(Some(Case::Pascal))))
    }

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String {
        scoped_identifier(self.escape_identifier(), self.namespace(), current_namespace)
    }

    /// Appends the provided prefix to the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    fn escape_scoped_identifier_with_prefix(&self, prefix: &str, current_namespace: &str) -> String {
        scoped_identifier(
            self.escape_identifier_with_prefix(prefix),
            self.namespace(),
            current_namespace,
        )
    }

    /// Concatenates the provided suffix on the definition's identifier, with scoping.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If the provided namespace is non-empty, the identifier's scope is qualified relative to it.
    fn escape_scoped_identifier_with_suffix(&self, suffix: &str, current_namespace: &str) -> String {
        scoped_identifier(
            self.escape_identifier_with_suffix(suffix),
            self.namespace(),
            current_namespace,
        )
    }

    /// Applies the provided prefix and suffix to the definition's identifier, with scoping.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If the provided namespace is non-empty, the identifier's scope is qualified relative to it.
    fn escape_scoped_identifier_with_prefix_and_suffix(
        &self,
        prefix: &str,
        suffix: &str,
        current_namespace: &str,
    ) -> String {
        scoped_identifier(
            self.escape_identifier_with_prefix_and_suffix(prefix, suffix),
            self.namespace(),
            current_namespace,
        )
    }

    fn obsolete_attribute(&self, check_parent: bool) -> Option<String> {
        self.get_deprecation(check_parent).map(|attribute| {
            let reason = if let Some(argument) = attribute {
                argument
            } else {
                format!("This {} has been deprecated", self.kind())
            };
            format!(r#"global::System.Obsolete("{reason}")"#)
        })
    }

    /// The helper name
    fn helper_name(&self, current_namespace: &str) -> String {
        self.escape_scoped_identifier_with_suffix("Helper", current_namespace)
    }

    /// The C# namespace of this entity.
    fn namespace(&self) -> String {
        let module_scope = &self.raw_scope().module_scope;

        // List of all recursive (it and its parents) cs::namespace attributes for this entity.
        let mut attribute_list = self
            .all_attributes()
            .into_iter()
            .map(|l| l.into_iter().find_map(match_cs_namespace))
            .collect::<Vec<_>>();
        // Reverse the list so that the top level module name is first.
        attribute_list.reverse();

        assert!(attribute_list.len() >= module_scope.len());

        module_scope
            .iter()
            .enumerate()
            .map(|(i, s)| match &attribute_list[i] {
                Some(namespace) => namespace.to_owned(),
                None => escape_keyword(&s.to_case(Case::Pascal)),
            })
            .collect::<Vec<_>>()
            .join(".")
    }

    /// The C# Type ID attribute.
    fn type_id_attribute(&self) -> String {
        format!(r#"IceRpc.Slice.TypeId("::{}")"#, self.module_scoped_identifier())
    }

    /// The C# access modifier to use. Returns "internal" if this entity has the cs::internal
    /// attribute otherwise returns "public".
    fn access_modifier(&self) -> String {
        if self.attributes(true).into_iter().find_map(match_cs_internal).is_some() {
            "internal".to_owned()
        } else {
            "public".to_owned()
        }
    }

    /// Returns the C# readonly modifier if this entity has the cs::readonly attribute otherwise
    /// returns None.
    fn readonly_modifier(&self) -> Option<String> {
        // Readonly is only valid for structs
        if self.attributes(true).into_iter().find_map(match_cs_readonly).is_some() {
            Some("readonly".to_owned())
        } else {
            None
        }
    }

    /// Returns the C# modifiers for this entity.
    fn modifiers(&self) -> String {
        if let Some(readonly) = self.readonly_modifier() {
            self.access_modifier() + " " + &readonly
        } else {
            self.access_modifier()
        }
    }

    /// If this entity has a doc comment with an overview on it, this returns the overview formatted as a C# summary,
    /// with any links resolved to the appropriate C# tag. Otherwise this returns `None`.
    fn formatted_doc_comment_summary(&self) -> Option<String> {
        self.comment().and_then(|comment| {
            comment.overview.as_ref().map(|overview| {
                format_message(&overview.message, |link| link.get_formatted_link(&self.namespace()))
            })
        })
    }

    /// Returns this entity's doc comment, formatted as a list of C# doc comment tag. The overview is converted to
    /// a `summary` tag, and any `@see` sections are converted to `seealso` tags. Any links present in these are
    /// resolved to the appropriate C# tag. If no doc comment is on this entity, this returns an empty vector.
    fn formatted_doc_comment(&self) -> Vec<CommentTag> {
        let mut comments = Vec::new();
        if let Some(comment) = self.comment() {
            // Add a summary comment tag if the comment contains an overview section.
            if let Some(overview) = comment.overview.as_ref() {
                let message = format_message(&overview.message, |link| link.get_formatted_link(&self.namespace()));
                comments.push(CommentTag::new("summary", message));
            }
            // Add a see-also comment tag for each '@see' tag in the comment.
            for see_tag in &comment.see {
                // We re-use `get_formatted_link` to correctly generate the link, then rip out the link.
                let formatted_link = see_tag.linked_entity().unwrap().get_formatted_link(&self.namespace());
                // The formatted link is always of the form `<tag attribute="link" />`.
                // We get the link from this by splitting the string on '"' characters, and taking the 2nd element.
                let link = formatted_link.split('"').nth(1).unwrap();
                comments.push(CommentTag::with_tag_attribute("seealso", "cref", link, String::new()));
            }
        }
        comments
    }

    /// This function is equivalent to [formatted_doc_comment](EntityExt::formatted_doc_comment), but it
    /// appends the provided string at the beginning of the `summary` tag returned from that function.
    /// If no `summary` tag was returned, this creates one that only contains the provided string.
    fn formatted_doc_comment_with_summary(&self, summary_content: String) -> Vec<CommentTag> {
        let mut comments = self.formatted_doc_comment();

        // Check if the doc comment already included a summary (it must be the first tag).
        // If it does have one, append the provided content at the beginning of it.
        // If it doesn't, create a new summary tag containing only the provided content.
        match comments.first_mut() {
            Some(summary) if &summary.tag == "summary" => {
                summary.content = summary_content + "\n" + &summary.content;
            }
            _ => comments.insert(0, CommentTag::new("summary", summary_content)),
        }
        comments
    }

    /// Returns a C# link tag that points to this entity from the provided namespace
    /// By default this uses a `<see cref="..." />` tag, but certain types override this default implementation
    /// to emit different kinds of links (like `<paramref name="..." />` for parameters).
    fn get_formatted_link(&self, namespace: &str) -> String {
        match self.concrete_entity() {
            Entities::Interface(interface_def) => {
                // For interface links, we always link to the client side interface (ex: `IMyInterface`).
                // TODO: add a way for users to link to an interface's proxy type: (ex: `MyInterfaceProxy`).
                let identifier = interface_def.interface_name();
                format!(r#"<see cref="{identifier}" />"#)
            }
            Entities::Operation(operation) => {
                // For operations, we link to the abstract method on the client side interface (ex: `IMyInterface`).
                let interface_name = operation.parent().unwrap().scoped_interface_name(namespace);
                let operation_name = operation.escape_identifier_with_suffix("Async");
                format!(r#"<see cref="{interface_name}.{operation_name}" />"#)
            }
            Entities::Parameter(parameter) => {
                // Parameter links use a different tag (`paramref`) in C# instead of the normal `see cref` tag.
                let identifier = parameter.parameter_name();
                format!(r#"<paramref name="{identifier}" />"#)
            }
            _ => {
                let identifier = self.escape_scoped_identifier(namespace);
                format!(r#"<see cref="{identifier}" />"#)
            }
        }
    }
}

impl<T: Entity + ?Sized> EntityExt for T {}
