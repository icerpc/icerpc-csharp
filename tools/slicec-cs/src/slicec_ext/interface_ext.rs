// Copyright (c) ZeroC, Inc. All rights reserved.

use super::named_symbol_ext::NamedSymbolExt;
use slice::grammar::{Interface, NamedSymbol};
use slice::util::{fix_case, CaseStyle};

pub trait InterfaceExt: NamedSymbolExt {
    /// The name of the generated C# interface for this Slice interface.
    /// eg. If the slice interface is `Foo`, the C# interface is `IFoo`.
    /// The name is always prefixed with `I` and the first letter is always
    /// capitalized.
    fn interface_name(&self) -> String;

    /// Name of the generated implementation struct for this Slice interface's proxy.
    /// eg. If the slice interface is `Foo`, the C# proxy implementation is `FooPrx`.
    fn proxy_implementation_name(&self) -> String {
        self.proxy_name().chars().skip(1).collect()
    }

    /// The name of the generated C# proxy struct for this Slice interface.
    /// eg. If the slice interface is `Foo`, the C# proxy is `IFooPrx`.
    fn proxy_name(&self) -> String {
        self.interface_name() + "Prx"
    }

    fn scoped_proxy_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.proxy_name()
        } else {
            format!("global::{}.{}", namespace, self.proxy_name())
        }
    }

    fn scoped_proxy_implementation_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.proxy_implementation_name()
        } else {
            format!("global::{}.{}", namespace, self.proxy_implementation_name())
        }
    }
}

impl InterfaceExt for Interface {
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
}
