// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_util::escape_keyword;
use slice::grammar::NamedSymbol;
use slice::util::{fix_case, CaseStyle};

pub trait NamedSymbolExt: NamedSymbol {
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
}

impl<T> NamedSymbolExt for T
where
    T: NamedSymbol + ?Sized,
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
        // TODO: not all types need to remove just one "::" (we use this currently for operations)
        let tokens = self
            .scope()
            .strip_prefix("::")
            .unwrap()
            .split("::")
            .map(|segment| escape_keyword(&fix_case(segment, CaseStyle::Pascal)))
            .collect::<Vec<_>>();

        // TODO: Workaround for operation scope
        let drop = match self.kind() {
            "operation" => match tokens.last().unwrap().as_str() {
                "_return" => 3,
                _ => 1,
            },
            "data member" => 1,
            _ => 0,
        };

        tokens[0..tokens.len() - drop].join(".")
    }
}
