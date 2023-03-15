// Copyright (c) ZeroC, Inc.

use super::entity_ext::EntityExt;
use crate::cs_attributes::match_cs_namespace;
use crate::cs_util::escape_keyword;
use slice::convert_case::{Case, Casing};
use slice::grammar::*;
pub trait ContainedExt<C: Entity>: Contained<C>
{
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

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String {
        scoped_identifier(&self.escape_identifier(), &self.namespace(), current_namespace)
    }

    /// Appends the provided prefix to the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: The case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    fn escape_scoped_identifier_with_prefix(&self, prefix: &str, current_namespace: &str) -> String {
        scoped_identifier(
            &self.escape_identifier_with_prefix(prefix),
            &self.namespace(),
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
            &self.escape_identifier_with_suffix(suffix),
            &self.namespace(),
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
            &self.escape_identifier_with_prefix_and_suffix(prefix, suffix),
            &self.namespace(),
            current_namespace,
        )
    }
}

impl<T: Contained<C> + ?Sized, C: Entity> ContainedExt<C> for T {}

fn scoped_identifier(identifier: &str, identifier_namespace: &str, current_namespace: &str) -> String {
    if current_namespace == identifier_namespace {
        identifier.to_owned()
    } else {
        format!("global::{identifier_namespace}.{identifier}")
    }
}