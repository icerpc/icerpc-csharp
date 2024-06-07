// Copyright (c) ZeroC, Inc.

use super::{scoped_identifier, InterfaceExt, MemberExt, ModuleExt};
use crate::cs_attributes::{CsAttribute, CsIdentifier, CsInternal, CsType};
use crate::cs_util::{escape_keyword, CsCase};
use convert_case::Case;
use slicec::grammar::attributes::Deprecated;
use slicec::grammar::*;

pub trait EntityExt: Entity {
    // Returns the C# identifier for the entity, which is either the identifier specified by the cs::identifier
    /// attribute as-is or the Slice identifier formatted with the specified casing.
    fn cs_identifier(&self, case: Case) -> String {
        self.find_attribute::<CsIdentifier>().map_or_else(
            || self.identifier().to_cs_case(case),
            |identifier_attribute| identifier_attribute.identifier.clone(),
        )
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

    fn obsolete_attribute(&self) -> Option<String> {
        self.find_attribute::<Deprecated>().map(|attribute| {
            let reason = match &attribute.reason {
                Some(reason) => reason.clone(),
                None => format!("This {} has been deprecated", self.kind()),
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
    fn access_modifier(&self) -> &str {
        match self.has_attribute::<CsInternal>() {
            true => "internal",
            false => "public",
        }
    }

    /// Returns a vector of C# attributes applied to this entity with `cs::attribute`.
    fn cs_attributes(&self) -> Vec<String> {
        self.find_attributes::<CsAttribute>()
            .into_iter()
            .map(|a| a.attribute.clone())
            .collect()
    }

    /// Returns a C# link tag that points to this entity from the provided namespace
    /// By default this uses a `<see cref="..." />` tag, but certain types override this default implementation
    /// to emit different kinds of links (like `<paramref name="..." />` for parameters).
    fn get_formatted_link(&self, namespace: &str) -> String {
        match self.concrete_entity() {
            Entities::Interface(interface_def) => {
                // For interface links, we always link to the client side interface (ex: `IMyInterface`).
                // TODO: add a way for users to link to an interface's proxy type: (ex: `MyInterfaceProxy`).
                let interface_name = interface_def.scoped_interface_name(namespace);
                format!(r#"<see cref="{interface_name}" />"#)
            }
            Entities::Operation(operation) => {
                // For operations, we link to the abstract method on the client side interface (ex: `IMyInterface`).
                let interface_name = operation.parent().scoped_interface_name(namespace);
                let operation_name = operation.escape_identifier_with_suffix("Async");
                format!(r#"<see cref="{interface_name}.{operation_name}" />"#)
            }
            Entities::Parameter(parameter) => {
                // Parameter links use a different tag (`paramref`) in C# instead of the normal `see cref` tag.
                let parameter_name = parameter.parameter_name();
                format!(r#"<paramref name="{parameter_name}" />"#)
            }
            Entities::Enumerator(enumerator) => {
                let enum_name = enumerator.parent().escape_scoped_identifier(namespace);
                let enumerator_name = enumerator.escape_identifier();
                format!(r#"<see cref="{enum_name}.{enumerator_name}" />"#)
            }
            Entities::CustomType(custom_type) => {
                let attribute = custom_type.find_attribute::<CsType>();
                format!(r#"<see cref="{}" />"#, attribute.unwrap().type_string)
            }
            Entities::Field(field) => {
                let parent_name = field.parent().escape_scoped_identifier(namespace);
                let field_name = field.escape_identifier();
                format!(r#"<see cref="{parent_name}.{field_name}" />"#)
            }
            _ => {
                let name = self.escape_scoped_identifier(namespace);
                format!(r#"<see cref="{name}" />"#)
            }
        }
    }
}

impl<T: Entity + ?Sized> EntityExt for T {}

// Unit tests for the `get_formatted_link` function.
#[cfg(test)]
mod formatted_link_tests {
    use super::EntityExt;
    use crate::cs_options::CsOptions;
    use slicec::compilation_state::CompilationState;
    use slicec::grammar::{Enumerator, Interface, Operation, Parameter, Struct};

    // TODO we should add some actual testing infrastructure to this crate.

    fn compile_slice(slice: &str) -> CompilationState {
        let options = &CsOptions::default().slice_options;
        slicec::compile_from_strings(&[slice], Some(options), |_| {}, |_| {})
    }

    #[test]
    fn unqualified_interface() {
        // Arrange
        let slice = "
            module Test
            interface MyInterface {}
        ";
        let ast = compile_slice(slice).ast;
        let interface_def = ast.find_element::<Interface>("Test::MyInterface").unwrap();

        // Act
        let interface_link = interface_def.get_formatted_link("");

        // Assert
        let expected = r#"<see cref="global::Test.IMyInterface" />"#;
        assert_eq!(interface_link, expected);
    }

    #[test]
    fn qualified_interface() {
        // Arrange
        let slice = "
            module Test
            interface MyInterface {}
        ";
        let ast = compile_slice(slice).ast;
        let interface_def = ast.find_element::<Interface>("Test::MyInterface").unwrap();

        // Act
        let interface_link = interface_def.get_formatted_link("Test");

        // Assert
        let expected = r#"<see cref="IMyInterface" />"#;
        assert_eq!(interface_link, expected);
    }

    #[test]
    fn unqualified_operation() {
        // Arrange
        let slice = "
            module Test
            interface MyInterface {
                myOperation()
            }
        ";
        let ast = compile_slice(slice).ast;
        let operation = ast.find_element::<Operation>("Test::MyInterface::myOperation").unwrap();

        // Act
        let operation_link = operation.get_formatted_link("");

        // Assert
        let expected = r#"<see cref="global::Test.IMyInterface.MyOperationAsync" />"#;
        assert_eq!(operation_link, expected);
    }

    #[test]
    fn qualified_operation() {
        // Arrange
        let slice = "
            module Test
            interface MyInterface {
                myOperation()
            }
        ";
        let ast = compile_slice(slice).ast;
        let operation = ast.find_element::<Operation>("Test::MyInterface::myOperation").unwrap();

        // Act
        let operation_link = operation.get_formatted_link("Test");

        // Assert
        let expected = r#"<see cref="IMyInterface.MyOperationAsync" />"#;
        assert_eq!(operation_link, expected);
    }

    // Parameters can only be linked to in their operation's doc comment, so there's no need to qualified links.
    #[test]
    fn unqualified_parameter() {
        // Arrange
        let slice = "
            module Test
            interface MyInterface {
                myOperation(myParam: bool)
            }
        ";
        let ast = compile_slice(slice).ast;
        let parameter = ast
            .find_element::<Parameter>("Test::MyInterface::myOperation::myParam")
            .unwrap();

        // Act
        let parameter_link = parameter.get_formatted_link("");

        // Assert
        let expected = r#"<paramref name="myParam" />"#;
        assert_eq!(parameter_link, expected);
    }

    #[test]
    fn unqualified_enumerator() {
        // Arrange
        let slice = "
            module Test
            enum MyEnum {
                Foo
            }
        ";
        let ast = compile_slice(slice).ast;
        let enumerator = ast.find_element::<Enumerator>("Test::MyEnum::Foo").unwrap();

        // Act
        let enumerator_link = enumerator.get_formatted_link("");

        // Assert
        let expected = r#"<see cref="global::Test.MyEnum.Foo" />"#;
        assert_eq!(enumerator_link, expected);
    }

    #[test]
    fn qualified_enumerator() {
        // Arrange
        let slice = "
            module Test
            enum MyEnum {
                Foo
            }
        ";
        let ast = compile_slice(slice).ast;
        let enumerator = ast.find_element::<Enumerator>("Test::MyEnum::Foo").unwrap();

        // Act
        let enumerator_link = enumerator.get_formatted_link("Test");

        // Assert
        let expected = r#"<see cref="MyEnum.Foo" />"#;
        assert_eq!(enumerator_link, expected);
    }

    // All other Slice types share the same code path, so testing structs is sufficient for testing everything else.
    #[test]
    fn unqualified_generic() {
        // Arrange
        let slice = "
            module Test
            struct MyStruct {}
        ";
        let ast = compile_slice(slice).ast;
        let struct_def = ast.find_element::<Struct>("Test::MyStruct").unwrap();

        // Act
        let struct_link = struct_def.get_formatted_link("");

        // Assert
        let expected = r#"<see cref="global::Test.MyStruct" />"#;
        assert_eq!(struct_link, expected);
    }

    // All other Slice types share the same code path, so testing structs is sufficient for testing everything else.
    #[test]
    fn qualified_generic() {
        // Arrange
        let slice = "
            module Test
            struct MyStruct {}
        ";
        let ast = compile_slice(slice).ast;
        let struct_def = ast.find_element::<Struct>("Test::MyStruct").unwrap();

        // Act
        let struct_link = struct_def.get_formatted_link("Test");

        // Assert
        let expected = r#"<see cref="MyStruct" />"#;
        assert_eq!(struct_link, expected);
    }
}
