// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::{match_cs_identifier, match_cs_internal, match_cs_namespace, match_cs_readonly};
use crate::cs_util::escape_keyword;

use slice::convert_case::{Case, Casing};
use slice::grammar::Entity;

pub trait EntityExt: Entity {
    // Returns the  C# identifier for the entity, which is either the Slice identifier formatted with the specified
    // casing or the identifier specified by the cs::identifier attribute if present.
    fn cs_identifier(&self, case: Option<Case>) -> String;

    /// Escapes and returns the definition's identifier, without any scoping.
    /// If the identifier is a C# keyword, a '@' prefix is appended to it.
    fn escape_identifier(&self) -> String;
    fn escape_identifier_with_prefix(&self, prefix: &str) -> String;
    fn escape_identifier_with_suffix(&self, suffix: &str) -> String;
    fn escape_identifier_with_prefix_and_suffix(&self, prefix: &str, suffix: &str) -> String;

    /// Escapes and returns the definition's identifier, fully scoped.
    /// If the identifier or any of the scopes are C# keywords, a '@' prefix is appended to them.
    /// Note: Case style is applied to all scope segments, not just the last one.
    ///
    /// If scope is non-empty, this also qualifies the identifier's scope relative to the provided
    /// one.
    fn escape_scoped_identifier(&self, current_namespace: &str) -> String;
    fn escape_scoped_identifier_with_prefix(&self, suffix: &str, current_namespace: &str) -> String;
    fn escape_scoped_identifier_with_suffix(&self, suffix: &str, current_namespace: &str) -> String;
    fn escape_scoped_identifier_with_prefix_and_suffix(
        &self,
        prefix: &str,
        suffix: &str,
        current_namespace: &str,
    ) -> String;

    /// Returns the interface name corresponding to this entity's identifier, without scoping.
    /// eg. If this entity's identifier is `foo`, the C# interface name is `IFoo`.
    /// The name is always prefixed with 'I' and the first letter is always
    /// capitalized.
    fn interface_name(&self) -> String;

    /// Returns the interface name corresponding to this entity's identifier, fully scoped.
    fn scoped_interface_name(&self, current_namespace: &str) -> String;

    fn obsolete_attribute(&self, check_parent: bool) -> Option<String>;

    /// The helper name
    fn helper_name(&self, current_namespace: &str) -> String;

    /// The C# namespace
    fn namespace(&self) -> String;

    /// The C# Type ID attribute.
    fn type_id_attribute(&self) -> String;

    /// The C# access modifier to use. Returns "internal" if this entity has the cs::internal
    /// attribute otherwise returns "public".
    fn access_modifier(&self) -> String;

    /// Returns the C# readonly modifier if this entity has the cs::readonly attribute otherwise
    /// returns None.
    fn readonly_modifier(&self) -> Option<String>;

    /// Returns the C# modifiers for this entity.
    fn modifiers(&self) -> String;
}

impl<T> EntityExt for T
where
    T: Entity + ?Sized,
{
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

    fn escape_identifier_with_prefix(&self, prefix: &str) -> String {
        escape_keyword(&format!("{}{}", prefix, self.cs_identifier(Some(Case::Pascal))))
    }

    fn escape_identifier_with_suffix(&self, suffix: &str) -> String {
        escape_keyword(&format!("{}{}", self.cs_identifier(Some(Case::Pascal)), suffix))
    }

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
        scoped_identifier(&self.escape_identifier(), &self.namespace(), current_namespace)
    }

    fn escape_scoped_identifier_with_prefix(&self, prefix: &str, current_namespace: &str) -> String {
        scoped_identifier(
            &self.escape_identifier_with_prefix(prefix),
            &self.namespace(),
            current_namespace,
        )
    }

    fn escape_scoped_identifier_with_suffix(&self, suffix: &str, current_namespace: &str) -> String {
        scoped_identifier(
            &self.escape_identifier_with_suffix(suffix),
            &self.namespace(),
            current_namespace,
        )
    }

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

    /// The helper name for this Entity
    fn helper_name(&self, namespace: &str) -> String {
        self.escape_scoped_identifier_with_suffix("Helper", namespace)
    }

    fn interface_name(&self) -> String {
        format!("I{}", self.cs_identifier(Some(Case::Pascal)))
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

    fn type_id_attribute(&self) -> String {
        format!(r#"IceRpc.Slice.TypeId("::{}")"#, self.module_scoped_identifier())
    }

    fn access_modifier(&self) -> String {
        if self.attributes(true).into_iter().find_map(match_cs_internal).is_some() {
            "internal".to_owned()
        } else {
            "public".to_owned()
        }
    }

    fn readonly_modifier(&self) -> Option<String> {
        // Readonly is only valid for structs
        if self.attributes(true).into_iter().find_map(match_cs_readonly).is_some() {
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

fn scoped_identifier(identifier: &str, identifier_namespace: &str, current_namespace: &str) -> String {
    if current_namespace == identifier_namespace {
        identifier.to_owned()
    } else {
        format!("global::{identifier_namespace}.{identifier}")
    }
}
