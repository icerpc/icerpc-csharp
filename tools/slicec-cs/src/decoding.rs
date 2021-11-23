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

    let mut bit_sequence_index: i64 = -1;
    let bit_sequence_size = get_bit_sequence_size(members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequence = decoder.DecodeBitSequence({});",
            bit_sequence_size
        );
        bit_sequence_index = 0;
    }

    // Decode required members
    for member in required_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&decode_member(
            member,
            &mut bit_sequence_index,
            namespace,
            &param,
        ));
    }

    // Decode tagged members
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&decode_tagged_member(member, namespace, &param));
    }

    assert!(bit_sequence_size == 0 || bit_sequence_index == bit_sequence_size as i64);

    code
}

fn decode_member(
    member: &impl Member,
    bit_sequence_index: &mut i64,
    namespace: &str,
    param: &str,
) -> CodeBlock {
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
                assert!(*bit_sequence_index >= 0);
                write!(code, "bitSequence[{}] ? ", *bit_sequence_index);
                *bit_sequence_index += 1;
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
                "new {}(decoder)",
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
                "{}.Decode{}(decoder)",
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
                    "decoder.DecodeArray<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming, true)
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than unmarshaling the collection elements one by one.
                if enum_def.is_unchecked {
                    format!(
                        "decoder.DecodeArray<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming, true)
                    )
                } else {
                    format!(
                        "\
decoder.DecodeArray(
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
    {}).ToArray()",
            decode_func(element_type, namespace).indent()
        )
    } else {
        match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.is_fixed_size() => {
                write!(
                    code,
                    "decoder.DecodeArray<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming, true)
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                if enum_def.is_unchecked {
                    write!(
                        code,
                        "decoder.DecodeArray<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming, true)
                    )
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeArray(
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
    {}).ToArray()",
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
                format!("decoder => decoder.DecodeNullablePrx<{}>()", type_name)
            } else {
                format!("decoder => new {}(decoder.DecodeProxy())", type_name)
            }
        }
        _ if type_ref.is_class_type() => {
            // is_class_type is either Typeref::Class or Primitive::AnyClass
            if type_ref.is_optional {
                format!(
                    "decoder => decoder.DecodeNullableClass<{}>()",
                    type_ref.to_type_string(namespace, TypeContext::Incoming, true)
                )
            } else {
                format!("decoder => decoder.DecodeClass<{}>()", type_name)
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            // Primitive::AnyClass is handled above by is_clas_type branch
            format!("decoder => decoder.Decode{}()", primitive_ref.type_suffix())
        }
        TypeRefs::Sequence(sequence_ref) => {
            format!(
                "\
decoder =>
    {}",
                decode_sequence(sequence_ref, namespace).indent()
            )
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            format!(
                "\
decoder =>
    {}",
                decode_dictionary(dictionary_ref, namespace).indent()
            )
        }
        TypeRefs::Enum(enum_ref) => {
            format!(
                "decoder => {}.Decode{}(decoder)",
                enum_ref.helper_name(namespace),
                enum_ref.identifier()
            )
        }
        TypeRefs::Struct(_) => {
            format!("decoder => new {}(decoder)", type_name)
        }
        TypeRefs::Class(_) => panic!("unexpected, see is_cass_type above"),
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

    let (all_members, non_streamed_members, stream_member) = if dispatch {
        (
            operation.parameters(),
            operation.nonstreamed_parameters(),
            operation.streamed_parameter(),
        )
    } else {
        (
            operation.return_members(),
            operation.nonstreamed_return_members(),
            operation.streamed_return_member(),
        )
    };

    let (required_members, tagged_members) = get_sorted_members(&non_streamed_members);

    let mut bit_sequence_index: i64 = -1;
    let bit_sequence_size = get_bit_sequence_size(&non_streamed_members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequence = decoder.DecodeBitSequence({});",
            bit_sequence_size
        );
        bit_sequence_index = 0;
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
                &mut bit_sequence_index,
                namespace,
                &member.parameter_name_with_prefix("iceP_"),
            )
        )
    }

    assert!(bit_sequence_index == -1 || bit_sequence_index == bit_sequence_size as i64);

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

    if let Some(stream_member) = stream_member {
        let param_type = stream_member.data_type();
        let param_type_str = param_type.to_type_string(namespace, TypeContext::Incoming, false);
        // Call to_type_string on the parameter itself to get its stream qualifier.
        let stream_type_str = stream_member.to_type_string(namespace, TypeContext::Incoming, false);

        let mut create_stream_param: CodeBlock = match param_type.concrete_type() {
            Types::Primitive(primitive) if matches!(primitive, Primitive::Byte) => {
                if dispatch {
                    "IceRpc.Slice.StreamParamReceiver.ToByteStream(request);".into()
                } else {
                    "streamParamReceiver!.ToByteStream();".into()
                }
            }
            _ => {
                if dispatch {
                    format!(
                        "\
IceRpc.Slice.StreamParamReceiver.ToAsyncEnumerable<{param_type}>(
    request,
    request.GetIceDecoderFactory(_defaultIceDecoderFactories),
    {decode_func});",
                        param_type = param_type_str,
                        decode_func = decode_func(param_type, namespace)
                    )
                    .into()
                } else {
                    format!(
                        "\
streamParamReceiver!.ToAsyncEnumerable<{param_type}>(
    response,
    invoker,
    response.GetIceDecoderFactory(_defaultIceDecoderFactories),
    {decode_func});",
                        param_type = param_type_str,
                        decode_func = decode_func(param_type, namespace)
                    )
                    .into()
                }
            }
        };

        writeln!(
            code,
            "{stream_param_type} {param_name} = {create_stream_param}",
            stream_param_type = stream_type_str,
            param_name = stream_member.parameter_name_with_prefix("iceP_"),
            create_stream_param = create_stream_param.indent()
        );
    }

    writeln!(code, "return {};", all_members.to_argument_tuple("iceP_"));

    code
}
