// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_util::escape_keyword;
use slice::code_gen_util::{fix_case, CaseStyle};
use slice::grammar::Entity;

pub trait EntityExt: Entity {
    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String;
    fn escape_identifier_with_prefix(&self, suffix: &str) -> String;
    fn escape_identifier_with_suffix(&self, suffix: &str) -> String;
    fn escape_identifier_with_prefix_and_suffix(&self, prefix: &str, suffix: &str) -> String;

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: Case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String;
    fn escape_scoped_identifier_with_prefix(&self, suffix: &str, current_namespace: &str)
        -> String;
    fn escape_scoped_identifier_with_suffix(&self, suffix: &str, current_namespace: &str)
        -> String;
    fn escape_scoped_identifier_with_prefix_and_suffix(
        &self,
        prefix: &str,
        suffix: &str,
        current_namespace: &str,
    ) -> String;

    /// Returns the interface name corresponding to this entity's identifier, without scoping.
    /// eg. If this entity's identifier is `foo`, the C# interface name is `IFoo`.
    /// The name is always prefixed with 'I' and the first letter is always
    /// capitalized. If the identifier is already in this format, it is returned unchanged.
    fn interface_name(&self) -> String;

    /// Returns the interface name corresponding to this entity's identifier, fully scoped.
    fn scoped_interface_name(&self, current_namepsace: &str) -> String;

    /// The helper name
    fn helper_name(&self, current_namespace: &str) -> String;

    /// The C# namespace
    fn namespace(&self) -> String;

    /// The C# Type Id attribute.
    fn type_id_attribute(&self) -> String;

    /// The C# access modifier to use. Returns "internal" if this entity has the cs:internal attribute otherwise
    /// returns "public".
    fn access_modifier(&self) -> String;

    /// Returns the C# readonly modifier if this entity has the cs:readonly attribute otherwise returns None.
    fn readonly_modifier(&self) -> Option<String>;

    /// Returns the C# modifiers for this entity.
    fn modifiers(&self) -> String;
}

impl<T> EntityExt for T
where
    T: Entity + ?Sized,
{
    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String {
        escape_keyword(&fix_case(self.identifier(), CaseStyle::Pascal))
    }

    fn escape_identifier_with_prefix(&self, prefix: &str) -> String {
        escape_keyword(&fix_case(
            &format!("{}{}", prefix, self.identifier()),
            CaseStyle::Pascal,
        ))
    }

    fn escape_identifier_with_suffix(&self, suffix: &str) -> String {
        escape_keyword(&fix_case(
            &format!("{}{}", self.identifier(), suffix),
            CaseStyle::Pascal,
        ))
    }

    fn escape_identifier_with_prefix_and_suffix(&self, prefix: &str, suffix: &str) -> String {
        escape_keyword(&fix_case(
            &format!("{}{}{}", prefix, self.identifier(), suffix),
            CaseStyle::Pascal,
        ))
    }

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if current_namespace == namespace {
            self.escape_identifier()
        } else {
            format!("global::{}.{}", namespace, self.escape_identifier())
        }
    }

    fn escape_scoped_identifier_with_prefix(
        &self,
        prefix: &str,
        current_namespace: &str,
    ) -> String {
        let namespace = self.namespace();
        if current_namespace == namespace {
            self.escape_identifier_with_prefix(prefix)
        } else {
            format!(
                "global::{}.{}",
                namespace,
                self.escape_identifier_with_prefix(prefix)
            )
        }
    }

    fn escape_scoped_identifier_with_suffix(
        &self,
        suffix: &str,
        current_namespace: &str,
    ) -> String {
        let namespace = self.namespace();
        if current_namespace == namespace {
            self.escape_identifier_with_suffix(suffix)
        } else {
            format!(
                "global::{}.{}",
                namespace,
                self.escape_identifier_with_suffix(suffix)
            )
        }
    }

    fn escape_scoped_identifier_with_prefix_and_suffix(
        &self,
        prefix: &str,
        suffix: &str,
        current_namespace: &str,
    ) -> String {
        let namespace = self.namespace();
        if current_namespace == namespace {
            self.escape_identifier_with_prefix_and_suffix(prefix, suffix)
        } else {
            format!(
                "global::{}.{}",
                namespace,
                self.escape_identifier_with_prefix_and_suffix(prefix, suffix)
            )
        }
    }

    /// The helper name for this Entity
    fn helper_name(&self, namespace: &str) -> String {
        self.escape_scoped_identifier_with_suffix("Helper", namespace)
    }

    fn interface_name(&self) -> String {
        let identifier = fix_case(self.identifier(), CaseStyle::Pascal);
        let mut chars = identifier.chars();

        // Check if the interface already follows the 'I' prefix convention.
        if identifier.chars().count() > 2
            && chars.next().unwrap() == 'I'
            && chars.next().unwrap().is_uppercase()
        {
            identifier.to_owned()
        } else {
            format!("I{}", identifier)
        }
    }

    fn scoped_interface_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if current_namespace == namespace {
            self.interface_name()
        } else {
            format!("global::{}.{}", namespace, self.interface_name())
        }
    }

    /// The C# namespace of this Entity
    fn namespace(&self) -> String {
        self.raw_scope()
            .module_scope
            .iter()
            .enumerate()
            .map(|(i, segment)| {
                let mut escaped_module = escape_keyword(&fix_case(segment, CaseStyle::Pascal));
                if i == 0 {
                    if let Some(attribute) = self.get_attribute("cs:namespace", true) {
                        escaped_module = attribute.first().unwrap().to_owned();
                    }
                }
                escaped_module
            })
            .collect::<Vec<_>>()
            .join(".")
    }

    fn type_id_attribute(&self) -> String {
        format!(
            r#"IceRpc.Slice.TypeId("::{}")"#,
            self.module_scoped_identifier()
        )
    }

    fn access_modifier(&self) -> String {
        if self.has_attribute("cs:internal", true) {
            "internal".to_owned()
        } else {
            "public".to_owned()
        }
    }

    fn readonly_modifier(&self) -> Option<String> {
        if self.has_attribute("cs:readonly", self.kind() == "data member") {
            Some("readonly".to_owned())
        } else {
            None
        }
    }

    fn modifiers(&self) -> String {
        if let Some(readonly) = self.readonly_modifier() {
            self.access_modifier() + " " + &readonly
        } else {
            self.access_modifier()
        }
    }
}
