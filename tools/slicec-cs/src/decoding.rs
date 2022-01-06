// Copyright (c) ZeroC, Inc. All rights reserved.
use crate::code_block::CodeBlock;
use crate::cs_util::*;
use crate::slicec_ext::*;
use slice::code_gen_util::*;
use slice::grammar::*;

pub fn decode_data_members(
    members: &[&DataMember],
    namespace: &str,
    field_type: FieldType,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let (required_members, tagged_members) = get_sorted_members(members);

    let bit_sequence_size = get_bit_sequence_size(members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({});",
            bit_sequence_size
        );
    }

    // Decode required members
    for member in required_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&decode_member(member, namespace, &param));
    }

    // Decode tagged members
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&decode_tagged_member(member, namespace, &param));
    }

    code
}

fn decode_member(member: &impl Member, namespace: &str, param: &str) -> CodeBlock {
    let mut code = CodeBlock::new();
    let data_type = member.data_type();
    let type_string = data_type.to_type_string(namespace, TypeContext::Incoming, true);

    write!(code, "{} = ", param);

    if data_type.is_optional {
        match data_type.concrete_type() {
            Types::Interface(_) => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullablePrx<{}>();", type_string);
                return code;
            }
            _ if data_type.is_class_type() => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullableClass<{}>();", type_string);
                return code;
            }
            _ => {
                write!(code, "bitSequenceReader.Read() ? ");
                // keep going
            }
        }
    }

    match &data_type.concrete_typeref() {
        TypeRefs::Interface(_) => {
            assert!(!data_type.is_optional);
            write!(code, "new {}(decoder.DecodeProxy())", type_string);
        }
        TypeRefs::Class(_) => {
            assert!(!data_type.is_optional);
            write!(code, "decoder.DecodeClass<{}>()", type_string);
        }
        TypeRefs::Primitive(primitive_ref) => {
            write!(code, "decoder.Decode{}()", primitive_ref.type_suffix());
        }
        TypeRefs::Struct(struct_ref) => {
            write!(
                code,
                "new {}(ref decoder)",
                struct_ref.escape_scoped_identifier(namespace),
            );
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            code.write(&decode_dictionary(dictionary_ref, namespace))
        }
        TypeRefs::Sequence(sequence_ref) => code.write(&decode_sequence(sequence_ref, namespace)),
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{}.Decode{}(ref decoder)",
                enum_ref.helper_name(namespace),
                enum_ref.identifier(),
            );
        }
    }

    if data_type.is_optional {
        code.write(" : null");
    }
    code.write(";");

    code
}

pub fn decode_tagged_member(member: &impl Member, namespace: &str, param: &str) -> CodeBlock {
    assert!(member.data_type().is_optional && member.tag().is_some());
    format!(
        "\
{param} = decoder.DecodeTagged(
    {tag},
    IceRpc.Slice.TagFormat.{tag_format},
    {decode_func});",
        param = param,
        tag = member.tag().unwrap(),
        tag_format = member.data_type().tag_format(),
        decode_func = decode_func(member.data_type(), namespace)
    )
    .into()
}

pub fn decode_dictionary(dictionary_ref: &TypeRef<Dictionary>, namespace: &str) -> CodeBlock {
    let key_type = &dictionary_ref.key_type;
    let value_type = &dictionary_ref.value_type;

    // decode key
    let mut decode_key = decode_func(key_type, namespace);

    // decode value
    let mut decode_value = decode_func(value_type, namespace);
    if matches!(
        value_type.concrete_type(),
        Types::Sequence(_) | Types::Dictionary(_)
    ) {
        write!(
            decode_value,
            " as {}",
            value_type.to_type_string(namespace, TypeContext::Nested, false)
        );
    }

    let method = match dictionary_ref.get_attribute("cs:generic", false) {
        Some(attributes) if attributes.first().unwrap() == "SortedDictionary" => {
            String::from("DecodeSortedDictionary")
        }
        _ => String::from("DecodeDictionary"),
    };

    if value_type.is_bit_sequence_encodable() {
        format!(
            "\
decoder.{method}WithBitSequence(
    minKeySize: {min_key_size},
    {decode_key},
    {decode_value})",
            method = method,
            min_key_size = key_type.min_wire_size(),
            decode_key = decode_key.indent(),
            decode_value = decode_value.indent()
        )
    } else {
        format!(
            "\
decoder.{method}(
    minKeySize: {min_key_size},
    minValueSize: {min_value_size},
    {decode_key},
    {decode_value})",
            method = method,
            min_key_size = key_type.min_wire_size(),
            min_value_size = value_type.min_wire_size(),
            decode_key = decode_key.indent(),
            decode_value = decode_value.indent()
        )
    }
    .into()
}

pub fn decode_sequence(sequence_ref: &TypeRef<Sequence>, namespace: &str) -> CodeBlock {
    let mut code = CodeBlock::new();
    let element_type = &sequence_ref.element_type;

    if let Some(generic_attribute) = sequence_ref.get_attribute("cs:generic", false) {
        let mut arg: CodeBlock = match element_type.concrete_type() {
            Types::Primitive(primitive)
                if primitive.is_numeric_or_bool() && primitive.is_fixed_size() =>
            {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than unmarshaling the collection elements one by one.
                format!(
                    "decoder.DecodeSequence<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming, true)
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than unmarshaling the collection elements one by one.
                if enum_def.is_unchecked {
                    format!(
                        "decoder.DecodeSequence<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming, true)
                    )
                } else {
                    format!(
                        "\
decoder.DecodeSequence(
    ({enum_type_name} e) => _ = {helper}.As{name}(({underlying_type})e))",
                        enum_type_name =
                            element_type.to_type_string(namespace, TypeContext::Incoming, false),
                        helper = enum_def.helper_name(namespace),
                        name = enum_def.identifier(),
                        underlying_type = enum_def.underlying_type().cs_keyword()
                    )
                }
            }
            _ => {
                if element_type.is_bit_sequence_encodable() {
                    format!(
                        "\
decoder.DecodeSequenceWithBitSequence(
    {})",
                        decode_func(element_type, namespace).indent()
                    )
                } else {
                    format!(
                        "\
decoder.DecodeSequence(
    minElementSize: {},
    {})",
                        element_type.min_wire_size(),
                        decode_func(element_type, namespace).indent()
                    )
                }
            }
        }
        .into();

        let mut block = CodeBlock::new();
        write!(
            code,
            "\
new {}(
    {})",
            sequence_ref.to_type_string(namespace, TypeContext::Incoming, true),
            match generic_attribute.first().unwrap().as_str() {
                "Stack" => {
                    write!(
                        block,
                        "\
global::System.Linq.Enumerable.Reverse(
    {})",
                        arg.indent()
                    );
                    block.indent()
                }
                _ => arg.indent(),
            }
        );
    } else if element_type.is_bit_sequence_encodable() {
        write!(
            code,
            "\
decoder.DecodeSequenceWithBitSequence(
    {})",
            decode_func(element_type, namespace).indent()
        )
    } else {
        match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.is_fixed_size() => {
                write!(
                    code,
                    "decoder.DecodeSequence<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming, true)
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                if enum_def.is_unchecked {
                    write!(
                        code,
                        "decoder.DecodeSequence<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming, true)
                    )
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeSequence(
    ({enum_type} e) => _ = {helper}.As{name}(({underlying_type})e))",
                        enum_type =
                            element_type.to_type_string(namespace, TypeContext::Incoming, false),
                        helper = enum_def.helper_name(namespace),
                        name = enum_def.identifier(),
                        underlying_type = enum_def.underlying_type().cs_keyword()
                    );
                }
            }
            _ => {
                write!(
                    code,
                    "\
decoder.DecodeSequence(
    minElementSize: {},
    {})",
                    element_type.min_wire_size(),
                    decode_func(element_type, namespace).indent()
                )
            }
        }
    }

    code
}

pub fn decode_func(type_ref: &TypeRef, namespace: &str) -> CodeBlock {
    // For value types the type declaration includes ? at the end, but the type name does not.
    let type_name = if type_ref.is_optional && type_ref.is_value_type() {
        type_ref.to_type_string(namespace, TypeContext::Incoming, true)
    } else {
        type_ref.to_type_string(namespace, TypeContext::Incoming, false)
    };

    let mut code: CodeBlock = match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if type_ref.is_optional {
                format!(
                    "(ref IceDecoder decoder) => decoder.DecodeNullablePrx<{}>()",
                    type_name
                )
            } else {
                format!(
                    "(ref IceDecoder decoder) => new {}(decoder.DecodeProxy())",
                    type_name
                )
            }
        }
        _ if type_ref.is_class_type() => {
            // is_class_type is either Typeref::Class or Primitive::AnyClass
            if type_ref.is_optional {
                format!(
                    "(ref IceDecoder decoder) => decoder.DecodeNullableClass<{}>()",
                    type_ref.to_type_string(namespace, TypeContext::Incoming, true)
                )
            } else {
                format!(
                    "(ref IceDecoder decoder) => decoder.DecodeClass<{}>()",
                    type_name
                )
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            // Primitive::AnyClass is handled above by is_clas_type branch
            format!(
                "(ref IceDecoder decoder) => decoder.Decode{}()",
                primitive_ref.type_suffix()
            )
        }
        TypeRefs::Sequence(sequence_ref) => {
            format!(
                "\
(ref IceDecoder decoder) =>
    {}",
                decode_sequence(sequence_ref, namespace).indent()
            )
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            format!(
                "\
(ref IceDecoder decoder) =>
    {}",
                decode_dictionary(dictionary_ref, namespace).indent()
            )
        }
        TypeRefs::Enum(enum_ref) => {
            format!(
                "(ref IceDecoder decoder) => {}.Decode{}(ref decoder)",
                enum_ref.helper_name(namespace),
                enum_ref.identifier()
            )
        }
        TypeRefs::Struct(_) => {
            format!("(ref IceDecoder decoder) => new {}(ref decoder)", type_name)
        }
        TypeRefs::Class(_) => panic!("unexpected, see is_class_type above"),
    }
    .into();

    if type_ref.is_optional && type_ref.is_value_type() {
        write!(code, " as {}?", type_name);
    }

    code
}

pub fn decode_operation(operation: &Operation, dispatch: bool) -> CodeBlock {
    let mut code = CodeBlock::new();

    let namespace = &operation.namespace();

    let non_streamed_members = if dispatch {
        operation.nonstreamed_parameters()
    } else {
        operation.nonstreamed_return_members()
    };

    assert!(!non_streamed_members.is_empty());

    let (required_members, tagged_members) = get_sorted_members(&non_streamed_members);

    let bit_sequence_size = get_bit_sequence_size(&non_streamed_members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({});",
            bit_sequence_size
        );
    }

    for member in required_members {
        writeln!(
            code,
            "{param_type} {decode}",
            param_type = member
                .data_type()
                .to_type_string(namespace, TypeContext::Incoming, false),
            decode = decode_member(
                member,
                namespace,
                &member.parameter_name_with_prefix("iceP_"),
            )
        )
    }

    for member in tagged_members {
        writeln!(
            code,
            "{param_type} {decode}",
            param_type = member
                .data_type()
                .to_type_string(namespace, TypeContext::Incoming, false),
            decode = decode_tagged_member(
                member,
                namespace,
                &member.parameter_name_with_prefix("iceP_"),
            )
        )
    }

    writeln!(
        code,
        "return {};",
        non_streamed_members.to_argument_tuple("iceP_")
    );

    code
}

pub fn decode_operation_stream(
    stream_member: &Parameter,
    namespace: &str,
    dispatch: bool,
    assign_to_variable: bool,
) -> CodeBlock {
    let param_type = stream_member.data_type();
    let param_type_str = param_type.to_type_string(namespace, TypeContext::Incoming, false);
    // Call to_type_string on the parameter itself to get its stream qualifier.
    let stream_type_str = stream_member.to_type_string(namespace, TypeContext::Incoming, false);

    let create_stream_param: CodeBlock = match param_type.concrete_type() {
        Types::Primitive(primitive) if matches!(primitive, Primitive::Byte) => {
            if dispatch {
                "request.Payload;".into()
            } else {
                "response.Payload;".into()
            }
        }
        _ => {
            if dispatch {
                format!(
                    "\
request.ToAsyncEnumerable<{param_type}>(
    _defaultActivator,
    {decode_func});",
                    param_type = param_type_str,
                    decode_func = decode_func(param_type, namespace).indent()
                )
                .into()
            } else {
                format!(
                    "\
response.ToAsyncEnumerable<{param_type}>(
    _defaultActivator,
    {decode_func});",
                    param_type = param_type_str,
                    decode_func = decode_func(param_type, namespace).indent()
                )
                .into()
            }
        }
    };

    if assign_to_variable {
        format!(
            "{stream_param_type} {param_name} = {create_stream_param}",
            stream_param_type = stream_type_str,
            param_name = stream_member.parameter_name_with_prefix("iceP_"),
            create_stream_param = create_stream_param
        )
        .into()
    } else {
        create_stream_param
    }
}
