// Copyright (c) ZeroC, Inc.

use super::{scoped_identifier, InterfaceExt, MemberExt, ModuleExt};
use crate::cs_attributes::{match_cs_identifier, match_cs_internal, match_cs_readonly};
use crate::cs_util::{escape_keyword, CsCase};
use convert_case::Case;
use slicec::grammar::*;

pub trait EntityExt: Entity {
    // Returns the C# identifier for the entity, which is either the the identifier specified by the cs::identifier
    /// attribute as-is or the Slice identifier formatted with the specified casing.
    fn cs_identifier(&self, case: Case) -> String {
        let identifier_attribute = self.attributes().into_iter().find_map(match_cs_identifier);

        match identifier_attribute {
            Some(identifier) => identifier,
            None => self.identifier().to_cs_case(case),
        }
    }

    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String {
        escape_keyword(&self.cs_identifier(Case::Pascal))
    }

    /// Appends the provided prefix to the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_prefix(&self, prefix: &str) -> String {
        escape_keyword(&format!("{}{}", prefix, self.cs_identifier(Case::Pascal)))
    }

    /// Concatenates the provided suffix on the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_suffix(&self, suffix: &str) -> String {
        escape_keyword(&format!("{}{}", self.cs_identifier(Case::Pascal), suffix))
    }

    /// Applies the provided prefix and suffix to the definition's identifier, without any scoping.
    /// If the resulting string is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier_with_prefix_and_suffix(&self, prefix: &str, suffix: &str) -> String {
        escape_keyword(&format!("{prefix}{}{suffix}", self.cs_identifier(Case::Pascal)))
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

    fn obsolete_attribute(&self) -> Option<String> {
        self.get_deprecation().map(|attribute| {
            let reason = if let Some(argument) = attribute {
                argument
            } else {
                format!("This {} has been deprecated", self.kind())
            };
            format!(r#"global::System.Obsolete("{reason}")"#)
        })
    }

    /// The C# namespace that this entity is contained within.
    fn namespace(&self) -> String {
        self.get_module().as_namespace()
    }

    /// The C# Type ID attribute.
    fn type_id_attribute(&self) -> String {
        format!(r#"SliceTypeId("::{}")"#, self.module_scoped_identifier())
    }

    /// The C# access modifier to use. Returns "internal" if this entity has the cs::internal
    /// attribute otherwise returns "public".
    fn access_modifier(&self) -> String {
        if self.attributes().into_iter().find_map(match_cs_internal).is_some() {
            "internal".to_owned()
        } else {
            "public".to_owned()
        }
    }

    /// Returns the C# readonly modifier if this entity has the cs::readonly attribute otherwise
    /// returns None.
    fn readonly_modifier(&self) -> Option<String> {
        // Readonly is only valid for structs
        if self.attributes().into_iter().find_map(match_cs_readonly).is_some() {
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
                let interface_name = operation.parent().scoped_interface_name(namespace);
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
