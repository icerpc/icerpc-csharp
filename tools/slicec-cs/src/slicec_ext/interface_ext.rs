// Copyright (c) ZeroC, Inc.

use super::{scoped_identifier, EntityExt};
use slice::convert_case::Case;
use slice::grammar::Interface;

pub trait InterfaceExt: EntityExt {
    /// Returns the interface name corresponding to this entity's identifier, without scoping.
    /// eg. If this entity's identifier is `foo`, the C# interface name is `IFoo`.
    /// The name is always prefixed with 'I' and the first letter is always
    /// capitalized.
    fn interface_name(&self) -> String {
        format!("I{}", self.cs_identifier(Case::Pascal))
    }

    /// The name of the generated C# service interface for this Slice interface.
    /// eg. If the Slice interface is `Foo`, the C# service interface is `IFooService`.
    fn service_name(&self) -> String {
        self.interface_name() + "Service"
    }

    /// Name of the generated proxy struct, e.g. if the Slice interface is `Foo`, the C# proxy is struct `FooProxy`.
    fn proxy_name(&self) -> String {
        self.cs_identifier(Case::Pascal) + "Proxy"
    }

    /// Returns the interface name corresponding to this entity's identifier, fully scoped.
    fn scoped_interface_name(&self, current_namespace: &str) -> String {
        scoped_identifier(self.interface_name(), self.namespace(), current_namespace)
    }

    fn scoped_service_name(&self, current_namespace: &str) -> String {
        scoped_identifier(self.service_name(), self.namespace(), current_namespace)
    }

    fn scoped_proxy_name(&self, current_namespace: &str) -> String {
        scoped_identifier(self.proxy_name(), self.namespace(), current_namespace)
    }
}

impl InterfaceExt for Interface {}
