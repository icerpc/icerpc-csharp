// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::*;
use slice::grammar::*;

use crate::code_block::CodeBlock;
use crate::cs_util::*;
use crate::slicec_ext::*;

pub fn encode_data_members(
    members: &[&DataMember],
    namespace: &str,
    field_type: FieldType,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let (required_members, tagged_members) = get_sorted_members(members);

    // Tagged members are encoded in a dictionary and don't count towards the optional bit sequence
    // size.
    let bit_sequence_size = get_bit_sequence_size(&required_members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = encoder.GetBitSequenceWriter({});",
            bit_sequence_size
        );
    }

    for member in required_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&encode_type(
            member.data_type(),
            TypeContext::DataMember,
            namespace,
            &param,
        ));
    }

    // Encode tagged
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&encode_tagged_type(
            member,
            namespace,
            &param,
            TypeContext::DataMember,
        ));
    }

    code
}

fn encode_type(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    param: &str,
) -> CodeBlock {
    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if type_ref.is_optional {
                format!("encoder.EncodeNullableProxy({}?.Proxy);", param)
            } else {
                format!("encoder.EncodeProxy({}.Proxy);", param)
            }
        }
        _ if type_ref.is_class_type() => {
            if type_ref.is_optional {
                format!("encoder.EncodeNullableClass({});", param)
            } else {
                format!("encoder.EncodeClass({});", param)
            }
        }
        concrete_typeref => {
            let value = if type_ref.is_optional && type_ref.is_value_type() {
                format!("{}.Value", param)
            } else {
                param.to_owned()
            };
            let encode_type = match concrete_typeref {
                TypeRefs::Primitive(primitive_ref) => {
                    format!("encoder.Encode{}({});", primitive_ref.type_suffix(), value)
                }
                TypeRefs::Struct(_) => format!("{}.Encode(ref encoder);", value),
                TypeRefs::Trait(_) => format!("{}.EncodeTrait(ref encoder);", param),
                TypeRefs::Sequence(sequence_ref) => format!(
                    "{};",
                    encode_sequence(sequence_ref, namespace, param, type_context),
                ),
                TypeRefs::Dictionary(dictionary_ref) => {
                    format!("{};", encode_dictionary(dictionary_ref, namespace, param))
                }
                TypeRefs::Enum(enum_ref) => format!(
                    "{helper}.Encode{name}(ref encoder, {param});",
                    helper = enum_ref.helper_name(namespace),
                    name = enum_ref.identifier(),
                    param = value,
                ),
                _ => panic!("class and proxy types are handled in the outer match"),
            };

            if type_ref.is_optional {
                // A null T[]? or List<T>? is implicitly converted into a default aka null
                // ReadOnlyMemory<T> or ReadOnlySpan<T>. Furthermore, the span of a default
                // ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which is distinct from
                // the span of an empty sequence. This is why the "value.Span != null" below
                // works correctly.
                format!(
                    "\
bitSequenceWriter.Write({param} != null);
if ({param} != null)
{{
    {encode_type}
}}
",
                    param = match concrete_typeref {
                        TypeRefs::Sequence(sequence_def)
                            if sequence_def.has_fixed_size_numeric_elements()
                                && !sequence_def.has_attribute("cs:generic", false)
                                && type_context == TypeContext::Outgoing =>
                            format!("{}.Span", param),
                        _ => param.to_owned(),
                    },
                    encode_type = encode_type
                )
            } else {
                encode_type
            }
        }
    }
    .into()
}

fn encode_tagged_type(
    member: &impl Member,
    namespace: &str,
    param: &str,
    type_context: TypeContext,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let data_type = member.data_type();

    assert!(data_type.is_optional && member.tag().is_some());

    let tag = member.tag().unwrap();

    let read_only_memory = match data_type.concrete_type() {
        Types::Sequence(sequence_def)
            if sequence_def.has_fixed_size_numeric_elements()
                && type_context == TypeContext::Outgoing
                && !data_type.has_attribute("cs:generic", false) =>
        {
            true
        }
        _ => false,
    };

    let value = if data_type.is_value_type() {
        format!("{}.Value", param)
    } else {
        param.to_owned()
    };

    // For types with a known size, we provide a size parameter with the size of the tagged
    // param/member:
    let (size_parameter, count_value) = match data_type.concrete_type() {
        Types::Primitive(primitive_def) if primitive_def.is_fixed_size() => {
            (Some(primitive_def.min_wire_size().to_string()), None)
        }
        Types::Primitive(primitive_def) if !matches!(primitive_def, Primitive::String) => {
            if primitive_def.is_unsigned_numeric() {
                (
                    Some(format!("IceEncoder.GetVarULongEncodedSize({})", value)),
                    None,
                )
            } else {
                (
                    Some(format!("IceEncoder.GetVarLongEncodedSize({})", value)),
                    None,
                )
            }
        }
        Types::Struct(struct_def) if struct_def.is_fixed_size() => {
            (Some(struct_def.min_wire_size().to_string()), None)
        }
        Types::Enum(enum_def) => {
            if let Some(underlying) = &enum_def.underlying {
                (Some(underlying.min_wire_size().to_string()), None)
            } else {
                (Some(format!("encoder.GetSizeLength((int){})", value)), None)
            }
        }
        Types::Sequence(sequence_def)
            if sequence_def.element_type.is_fixed_size()
                && !sequence_def.element_type.is_optional =>
        {
            if read_only_memory {
                (
                    Some(format!(
                        "encoder.GetSizeLength({value}.Length) + {element_min_wire_size} * {value}.Length",
                        value = value,
                        element_min_wire_size = sequence_def.element_type.min_wire_size()
                    )),
                    None,
                )
            } else {
                (
                    Some(format!(
                        "encoder.GetSizeLength(count) + {} * count",
                        sequence_def.element_type.min_wire_size()
                    )),
                    Some(value.clone()),
                )
            }
        }
        Types::Dictionary(dictionary_def)
            if dictionary_def.key_type.is_fixed_size()
                && !dictionary_def.key_type.is_optional
                && dictionary_def.value_type.is_fixed_size()
                && !dictionary_def.value_type.is_optional =>
        {
            (
                Some(format!(
                    "encoder.GetSizeLength(count) + {min_wire_size} * count",
                    min_wire_size = dictionary_def.key_type.min_wire_size()
                        + dictionary_def.value_type.min_wire_size()
                )),
                Some(value.clone()),
            )
        }
        _ => (None, None),
    };

    let mut args = vec![];
    args.push(tag.to_string());

    args.push(format!("IceRpc.Slice.TagFormat.{}", data_type.tag_format()));
    if let Some(size_parameter) = size_parameter {
        args.push("size: ".to_owned() + &size_parameter);
    }
    args.push(value);
    args.push(
        encode_action(&clone_as_non_optional(data_type), type_context, namespace).to_string(),
    );

    writeln!(
        code,
        "\
if ({param} != null)
{{
    {encode}
}}",
        param = if read_only_memory {
            param.to_owned() + ".Span"
        } else {
            param.to_owned()
        },
        encode = {
            let mut code = CodeBlock::new();
            if let Some(value) = count_value {
                writeln!(code, "int count = {}.Count();", value);
            }
            writeln!(code, "encoder.EncodeTagged({});", args.join(", "));
            code
        }
        .indent()
    );

    code
}

fn encode_sequence(
    sequence_ref: &TypeRef<Sequence>,
    namespace: &str,
    value: &str,
    type_context: TypeContext,
) -> CodeBlock {
    let has_custom_type = sequence_ref.has_attribute("cs:generic", false);
    if sequence_ref.has_fixed_size_numeric_elements() {
        if type_context == TypeContext::Outgoing && !has_custom_type {
            format!("encoder.EncodeSpan({}.Span)", value)
        } else {
            format!("encoder.EncodeSequence({})", value)
        }
    } else {
        format!(
            "\
encoder.EncodeSequence{with_bit_sequence}(
    {param},
    {encode_action})",
            with_bit_sequence = if sequence_ref.element_type.is_bit_sequence_encodable() {
                "WithBitSequence"
            } else {
                ""
            },
            param = value,
            encode_action =
                encode_action(&sequence_ref.element_type, TypeContext::Nested, namespace).indent()
        )
    }
    .into()
}

fn encode_dictionary(dictionary_def: &Dictionary, namespace: &str, param: &str) -> CodeBlock {
    format!(
        "\
encoder.{method}(
    {param},
    {encode_key},
    {encode_value})",
        method = if dictionary_def.value_type.is_bit_sequence_encodable()
            && dictionary_def.value_type.is_optional
        {
            "EncodeDictionaryWithBitSequence"
        } else {
            "EncodeDictionary"
        },
        param = param,
        encode_key =
            encode_action(&dictionary_def.key_type, TypeContext::Nested, namespace).indent(),
        encode_value =
            encode_action(&dictionary_def.value_type, TypeContext::Nested, namespace).indent()
    )
    .into()
}

pub fn encode_action(type_ref: &TypeRef, type_context: TypeContext, namespace: &str) -> CodeBlock {
    let mut code = CodeBlock::new();
    let is_optional = type_ref.is_optional;

    let value = match (type_ref.is_optional, type_ref.is_value_type()) {
        (true, false) => "value!",
        (true, true) => "value!.Value",
        _ => "value",
    };
    let value_type = type_ref.to_type_string(namespace, type_context, false);

    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if is_optional {
                write!(
                    code,
                    "(ref IceEncoder encoder, {} value) => encoder.EncodeNullableProxy(value?.Proxy)",
                    value_type
                );
            } else {
                write!(
                    code,
                    "(ref IceEncoder encoder, {} value) => encoder.EncodeProxy(value.Proxy)",
                    value_type
                );
            }
        }
        TypeRefs::Class(_) => {
            if is_optional {
                write!(
                    code,
                    "(ref IceEncoder encoder, {} value) => encoder.EncodeNullableClass(value)",
                    value_type
                );
            } else {
                write!(
                    code,
                    "(ref IceEncoder encoder, {} value) => encoder.EncodeClass(value)",
                    value_type
                );
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => encoder.Encode{builtin_type}({value})",
                value_type = value_type,
                builtin_type = primitive_ref.type_suffix(),
                value = value
            )
        }
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => {helper}.Encode{name}(ref encoder, {value})",
                value_type = value_type,
                helper = enum_ref.helper_name(namespace),
                name = enum_ref.identifier(),
                value = value
            )
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => {encode_dictionary}",
                value_type = value_type,
                encode_dictionary = encode_dictionary(dictionary_ref, namespace, "value")
            );
        }
        TypeRefs::Sequence(sequence_ref) => {
            // We generate the sequence encoder inline, so this function must not be called when
            // the top-level object is not cached.
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => {encode_sequence}",
                value_type = value_type,
                encode_sequence = encode_sequence(sequence_ref, namespace, "value", type_context)
            )
        }
        TypeRefs::Struct(_) => {
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => {value}.Encode(ref encoder)",
                value_type = value_type,
                value = value
            )
        }
        TypeRefs::Trait(_) => {
            write!(
                code,
                "(ref IceEncoder encoder, {value_type} value) => value.EncodeTrait(ref encoder)",
                value_type = value_type,
            )
        }
    }

    code
}

pub fn encode_operation(operation: &Operation, return_type: bool) -> CodeBlock {
    let mut code = CodeBlock::new();
    let namespace = &operation.namespace();

    let members = if return_type {
        operation.nonstreamed_return_members()
    } else {
        operation.nonstreamed_parameters()
    };

    let (required_members, tagged_members) = get_sorted_members(&members);

    let bit_sequence_size = get_bit_sequence_size(&members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = encoder.GetBitSequenceWriter({});",
            bit_sequence_size
        );
    }

    for member in required_members {
        code.writeln(&encode_type(
            member.data_type(),
            TypeContext::Outgoing,
            namespace,
            &match members.as_slice() {
                [_] => "value".to_owned(),
                _ => format!("value.{}", &member.field_name(FieldType::NonMangled)),
            },
        ));
    }

    for member in tagged_members {
        code.writeln(&encode_tagged_type(
            member,
            namespace,
            &match members.as_slice() {
                [_] => "value".to_owned(),
                _ => format!("value.{}", &member.field_name(FieldType::NonMangled)),
            },
            TypeContext::Outgoing,
        ));
    }

    code
}
