// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_util::escape_keyword;
use slice::code_gen_util::{fix_case, CaseStyle};
use slice::grammar::Entity;

pub trait EntityExt: Entity {
    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String;
    fn escape_identifier_with_suffix(&self, suffix: &str) -> String;

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: Case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String;
    fn escape_scoped_identifier_with_suffix(&self, suffix: &str, current_namespace: &str)
        -> String;

    /// The helper name
    fn helper_name(&self, namespace: &str) -> String;

    /// The C# namespace
    fn namespace(&self) -> String;

    /// The C# Type Id attribute.
    fn type_id_attribute(&self) -> String;
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

    fn escape_identifier_with_suffix(&self, suffix: &str) -> String {
        escape_keyword(&fix_case(
            &format!("{}{}", self.identifier(), suffix),
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

    /// The helper name for this NamedSymbol
    fn helper_name(&self, namespace: &str) -> String {
        self.escape_scoped_identifier_with_suffix("Helper", namespace)
    }

    /// The C# namespace of this NamedSymbol
    fn namespace(&self) -> String {
        // TODO: check metadata
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
            r#"IceRpc.Slice.TypeId("{}")"#,
            self.module_scoped_identifier()
        )
    }
}
