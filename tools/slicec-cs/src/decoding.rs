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
        let decode_member = decode_member(member, &mut bit_sequence_index, namespace, &param);
        code.writeln(&decode_member);
    }

    // Decode tagged members
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&decode_tagged_member(member, namespace, &param));
    }

    assert!(bit_sequence_size == 0 || bit_sequence_index == bit_sequence_size as i64);

    code
}

pub fn decode_member(
    member: &impl Member,
    bit_sequence_index: &mut i64,
    namespace: &str,
    param: &str,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let data_type = member.data_type();
    let type_string = data_type.to_non_optional_type_string(namespace, TypeContext::Incoming);

    write!(code, "{} = ", param);

    if data_type.is_optional {
        match data_type.concrete_type() {
            Types::Interface(_) => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullablePrx<{}>();", type_string);
                return code;
            }
            Types::Class(_) => {
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

    match data_type.concrete_type() {
        Types::Interface(_) => {
            assert!(!data_type.is_optional);
            write!(code, "new {}(decoder.DecodeProxy());", type_string);
        }
        Types::Class(_) => {
            assert!(!data_type.is_optional);
            write!(code, "decoder.DecodeClass<{}>();", type_string);
        }
        Types::Primitive(primitive) => {
            write!(code, "decoder.Decode{}()", primitive.type_suffix());
        }
        Types::Struct(struct_def) => {
            write!(
                code,
                "new {}(decoder)",
                struct_def.escape_scoped_identifier(namespace),
            );
        }
        Types::Dictionary(dictionary) => {
            code.write(&decode_dictionary(data_type, dictionary, namespace))
        }
        Types::Sequence(sequence) => code.write(&decode_sequence(data_type, sequence, namespace)),
        Types::Enum(enum_def) => {
            write!(
                code,
                "{}.Decode{}(decoder)",
                enum_def.helper_name(namespace),
                enum_def.identifier(),
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
        "{param} = decoder.DecodeTagged({tag}, IceRpc.Slice.TagFormat.{tag_format}, {decode_func});",
        param = param,
        tag = member.tag().unwrap(),
        tag_format = member.data_type().tag_format(),
        decode_func = decode_func(member.data_type(), namespace)
    )
    .into()
}

pub fn decode_dictionary(
    type_ref: &TypeRef,
    dictionary_def: &Dictionary,
    namespace: &str,
) -> CodeBlock {
    let value_type = &dictionary_def.value_type;
    let with_bit_sequence = value_type.is_bit_sequence_encodable();

    let mut args = vec![format!(
        "minKeySize: {}",
        dictionary_def.key_type.min_wire_size()
    )];

    if !with_bit_sequence {
        args.push(format!("minValueSize: {}", value_type.min_wire_size()));
    }

    if with_bit_sequence && value_type.is_reference_type() {
        args.push("withBitSequence: true".to_owned());
    }

    // decode key
    args.push(decode_func(&dictionary_def.key_type, namespace).to_string());

    // decode value
    let mut decode_value = decode_func(value_type, namespace);
    if matches!(
        value_type.concrete_type(),
        Types::Sequence(_) | Types::Dictionary(_)
    ) {
        write!(
            decode_value,
            " as {}",
            value_type.to_type_string(namespace, TypeContext::Nested)
        );
    }
    args.push(decode_value.to_string());

    let mut code = CodeBlock::new();
    write!(
        code,
        "decoder.{method}({args})",
        method = match type_ref.get_attribute("cs:generic", false) {
            Some(attributes) if attributes.first().unwrap() == "SortedDictionary" =>
                "DecodeSortedDictionary",
            _ => "DecodeDictionary",
        },
        args = args.join(", ")
    );
    code
}

pub fn decode_sequence(type_ref: &TypeRef, sequence: &Sequence, namespace: &str) -> CodeBlock {
    let mut code = CodeBlock::new();
    let element_type = &sequence.element_type;

    if let Some(generic_attribute) = type_ref.get_attribute("cs:generic", false) {
        let args: String;
        assert!(!generic_attribute.is_empty());

        match element_type.concrete_type() {
            Types::Primitive(primitive)
                if primitive.is_numeric_or_bool() && primitive.is_fixed_size() =>
            {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than unmarshaling the collection elements one by one.
                args = format!(
                    "decoder.DecodeArray<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming)
                );
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than unmarshaling the collection elements one by one.
                if enum_def.is_unchecked {
                    args = format!(
                        "decoder.DecodeArray<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming)
                    );
                } else {
                    args = format!(
                        "decoder.DecodeArray(({enum_type_name} e) => _ = {helper}.As{name}(({underlying_type})e))",
                        enum_type_name = element_type.to_type_string(namespace, TypeContext::Incoming),
                        helper = enum_def.helper_name(namespace),
                        name = enum_def.identifier(),
                        underlying_type = enum_def.underlying_type().cs_keyword()
                    );
                }
            }
            _ => {
                if element_type.is_optional && element_type.is_bit_sequence_encodable() {
                    args = format!(
                        "decoder.DecodeSequence({}{})",
                        if element_type.is_reference_type() {
                            "withBitSequence: true, "
                        } else {
                            ""
                        },
                        decode_func(element_type, namespace)
                    );
                } else {
                    args = format!(
                        "decoder.DecodeSequence(minElementSize: {}, {})",
                        element_type.min_wire_size(),
                        decode_func(element_type, namespace)
                    );
                }
            }
        }

        write!(
            code,
            "new {}({})",
            type_ref.to_type_string(namespace, TypeContext::Incoming),
            match generic_attribute.first().unwrap().as_str() {
                "Stack" => format!("global::System.Linq.Enumerable.Reverse({})", args),
                _ => args,
            }
        );
    } else {
        match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.is_fixed_size() => {
                write!(
                    code,
                    "decoder.DecodeArray<{}>()",
                    element_type.to_type_string(namespace, TypeContext::Incoming)
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                if enum_def.is_unchecked {
                    write!(
                        code,
                        "decoder.DecodeArray<{}>()",
                        element_type.to_type_string(namespace, TypeContext::Incoming)
                    )
                } else {
                    write!(
                        code,
                        "decoder.DecodeArray(({enum_type} e) => _ = {helper}.As{name}(({underlying_type})e))",
                        enum_type = element_type.to_type_string(namespace, TypeContext::Incoming),
                        helper = enum_def.helper_name(namespace),
                        name = enum_def.identifier(),
                        underlying_type = enum_def.underlying_type().cs_keyword());
                }
            }
            _ => {
                write!(
                    code,
                    "decoder.DecodeSequence({}).ToArray()",
                    if element_type.is_optional && element_type.is_bit_sequence_encodable() {
                        format!(
                            "{}{}",
                            if element_type.is_reference_type() {
                                "withBitSequence: true, "
                            } else {
                                ""
                            },
                            decode_func(element_type, namespace)
                        )
                    } else {
                        format!(
                            "minElementSize:{}, {}",
                            element_type.min_wire_size(),
                            decode_func(element_type, namespace)
                        )
                    }
                );
            }
        }
    }

    code
}

pub fn decode_func(type_ref: &TypeRef, namespace: &str) -> CodeBlock {
    let mut code = CodeBlock::new();

    // For value types the type declaration includes ? at the end, but the type name does not.
    let type_name = if type_ref.is_optional && type_ref.is_value_type() {
        type_ref.to_non_optional_type_string(namespace, TypeContext::Incoming)
    } else {
        type_ref.to_type_string(namespace, TypeContext::Incoming)
    };

    match type_ref.concrete_type() {
        Types::Interface(_) => {
            if type_ref.is_optional {
                write!(
                    code,
                    "decoder => decoder.DecodeNullablePrx<{}>()",
                    type_name
                );
            } else {
                write!(code, "decoder => new {}(decoder.DecodeProxy())", type_name);
            }
        }
        Types::Class(_) => {
            if type_ref.is_optional {
                write!(
                    code,
                    "decoder => decoder.DecodeNullableClass<{}>()",
                    type_name
                );
            } else {
                write!(code, "decoder => decoder.DecodeClass<{}>()", type_name);
            }
        }
        Types::Primitive(primitive) => {
            write!(
                code,
                "decoder => decoder.Decode{}()",
                primitive.type_suffix()
            );
        }
        Types::Sequence(sequence) => {
            write!(
                code,
                "decoder => {}",
                decode_sequence(type_ref, sequence, namespace)
            );
        }
        Types::Dictionary(dictionary) => {
            write!(
                code,
                "decoder => {}",
                decode_dictionary(type_ref, dictionary, namespace)
            );
        }
        Types::Enum(enum_def) => {
            write!(
                code,
                "decoder => {}.Decode{}(decoder)",
                enum_def.helper_name(namespace),
                enum_def.identifier()
            );
        }
        Types::Struct(_) => {
            write!(code, "decoder => new {}(decoder)", type_name);
        }
    }

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
                .to_type_string(namespace, TypeContext::Incoming),
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
                .to_type_string(namespace, TypeContext::Incoming),
            decode = decode_tagged_member(
                member,
                namespace,
                &member.parameter_name_with_prefix("iceP_"),
            )
        )
    }

    if let Some(stream_member) = stream_member {
        let param_type = stream_member.data_type();
        let param_type_str = param_type.to_type_string(namespace, TypeContext::Incoming);
        // Call to_type_string on the parameter itself to get its stream qualifier.
        let stream_type_str = stream_member.to_type_string(namespace, TypeContext::Incoming);

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
