// Copyright (c) ZeroC, Inc.

use slicec::grammar::{Attribute, AttributeKind, LanguageKind};

pub const ATTRIBUTE: &str = "cs::attribute";
pub const ENCODED_RESULT: &str = "cs::encodedResult";
pub const GENERIC: &str = "cs::generic";
pub const IDENTIFIER: &str = "cs::identifier";
pub const INTERNAL: &str = "cs::internal";
pub const NAMESPACE: &str = "cs::namespace";
pub const READONLY: &str = "cs::readonly";
pub const CUSTOM: &str = "cs::custom";
pub const ATTRIBUTE_PREFIX: &str = "cs::";

#[derive(Debug)]
pub enum CsAttributeKind {
    Attribute { attribute: String },
    EncodedResult,
    Generic { generic_type: String },
    Identifier { identifier: String },
    Internal,
    Namespace { namespace: String },
    Readonly,
    Custom { name: String },
}

impl LanguageKind for CsAttributeKind {
    fn directive(&self) -> &str {
        match &self {
            CsAttributeKind::Attribute { .. } => ATTRIBUTE,
            CsAttributeKind::EncodedResult => ENCODED_RESULT,
            CsAttributeKind::Generic { .. } => GENERIC,
            CsAttributeKind::Identifier { .. } => IDENTIFIER,
            CsAttributeKind::Internal => INTERNAL,
            CsAttributeKind::Namespace { .. } => NAMESPACE,
            CsAttributeKind::Readonly => READONLY,
            CsAttributeKind::Custom { .. } => CUSTOM,
        }
    }

    fn as_any(&self) -> &dyn std::any::Any {
        self
    }

    fn is_repeatable(&self) -> bool {
        match &self {
            CsAttributeKind::Attribute { .. } => true,
            CsAttributeKind::EncodedResult => false,
            CsAttributeKind::Generic { .. } => false,
            CsAttributeKind::Identifier { .. } => false,
            CsAttributeKind::Internal => false,
            CsAttributeKind::Namespace { .. } => false,
            CsAttributeKind::Readonly => false,
            CsAttributeKind::Custom { .. } => false,
        }
    }
}

pub fn as_cs_attribute(attribute: &Attribute) -> Option<&CsAttributeKind> {
    // `LanguageKind`s are created by slicec-cs (not slicec), so any `LanguageKind`s MUST be `CsLanguageKind`s.
    if let AttributeKind::LanguageKind { kind } = &attribute.kind {
        Some(kind.as_any().downcast_ref::<CsAttributeKind>().unwrap())
    } else {
        None
    }
}

pub fn match_cs_attribute(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Attribute { attribute } => Some(attribute.clone()),
        _ => None,
    })
}

pub fn match_cs_encoded_result(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::EncodedResult => Some(()),
        _ => None,
    })
}

pub fn match_cs_generic(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Generic { generic_type } => Some(generic_type.clone()),
        _ => None,
    })
}

pub fn match_cs_identifier(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Identifier { identifier } => Some(identifier.to_owned()),
        _ => None,
    })
}

pub fn match_cs_internal(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Internal => Some(()),
        _ => None,
    })
}

pub fn match_cs_namespace(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Namespace { namespace } => Some(namespace.to_owned()),
        _ => None,
    })
}

pub fn match_cs_readonly(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Readonly => Some(()),
        _ => None,
    })
}

pub fn match_cs_custom(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Custom { name } => Some(name.to_owned()),
        _ => None,
    })
}
