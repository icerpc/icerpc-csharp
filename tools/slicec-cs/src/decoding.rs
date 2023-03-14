// Copyright (c) ZeroC, Inc.

use crate::builders::{Builder, FunctionCallBuilder};
use crate::cs_attributes::match_cs_generic;
use crate::cs_util::*;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::convert_case::{Case, Casing};
use slice::grammar::*;
use slice::utils::code_gen_util::*;

pub fn decode_fields(fields: &[&Field], namespace: &str, field_type: FieldType, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();

    let (required_fields, tagged_fields) = get_sorted_members(fields);

    let bit_sequence_size = get_bit_sequence_size(encoding, fields);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({bit_sequence_size});",
        );
    }

    // Decode required fields
    for field in required_fields {
        let param = format!("this.{}", field.field_name(field_type));
        code.writeln(&decode_member(field, namespace, &param, encoding));
    }

    // Decode tagged data fields
    for field in tagged_fields {
        let param = format!("this.{}", field.field_name(field_type));
        code.writeln(&decode_tagged(field, namespace, &param, true, encoding));
    }

    code
}

pub fn default_activator(encoding: Encoding) -> &'static str {
    if encoding == Encoding::Slice1 {
        "_defaultActivator"
    } else {
        "null"
    }
}

fn decode_member(member: &impl Member, namespace: &str, param: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let data_type = member.data_type();
    let type_string = data_type.cs_type_string(namespace, TypeContext::Decode, true);

    write!(code, "{param} = ");

    if data_type.is_optional {
        match data_type.concrete_type() {
            Types::Interface(_) if encoding == Encoding::Slice1 => {
                writeln!(code, "decoder.DecodeNullableProxy<{type_string}>();");
                return code;
            }
            Types::Primitive(Primitive::ServiceAddress) if encoding == Encoding::Slice1 => {
                writeln!(code, "decoder.DecodeNullableServiceAddress();");
                return code;
            }
            _ if data_type.is_class_type() => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullableClass<{type_string}>();");
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
            write!(code, "decoder.DecodeProxy<{type_string}>()");
        }
        TypeRefs::Class(_) => {
            assert!(!data_type.is_optional);
            write!(code, "decoder.DecodeClass<{type_string}>()");
        }
        TypeRefs::Primitive(primitive_ref) => {
            if primitive_ref.is_class_type() {
                write!(code, "decoder.DecodeClass<SliceClass>()");
            } else {
                write!(code, "decoder.Decode{}()", primitive_ref.type_suffix());
            }
        }
        TypeRefs::Struct(_) => write!(code, "new {type_string}(ref decoder)"),
        TypeRefs::Exception(_) => write!(code, "new {type_string}(ref decoder)"),
        TypeRefs::Dictionary(dictionary_ref) => code.write(&decode_dictionary(dictionary_ref, namespace, encoding)),
        TypeRefs::Sequence(sequence_ref) => code.write(&decode_sequence(sequence_ref, namespace, encoding)),
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Some(Case::Pascal)),
            );
        }
        TypeRefs::CustomType(custom_type_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = custom_type_ref.cs_identifier(Some(Case::Pascal)),
            );
        }
    }

    if data_type.is_optional {
        code.write(" : null");
    }
    code.write(";");

    code
}

pub fn decode_tagged(
    member: &impl Member,
    namespace: &str,
    param: &str,
    use_tag_end_marker: bool,
    encoding: Encoding,
) -> CodeBlock {
    let data_type = member.data_type();

    assert!(data_type.is_optional);
    assert!(member.tag().is_some());

    let decode = FunctionCallBuilder::new("decoder.DecodeTagged")
        .add_argument(member.tag().unwrap())
        .add_argument_if(encoding == Encoding::Slice1, || {
            format!("IceRpc.Slice.TagFormat.{}", data_type.tag_format().unwrap())
        })
        .add_argument(decode_func(data_type, namespace, encoding))
        .add_argument(format!("useTagEndMarker: {use_tag_end_marker}"))
        .build();

    format!("{param} = {decode};").into()
}

pub fn decode_dictionary(dictionary_ref: &TypeRef<Dictionary>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let key_type = &dictionary_ref.key_type;
    let value_type = &dictionary_ref.value_type;

    // decode key
    let mut decode_key = decode_func(key_type, namespace, encoding);

    // decode value
    let mut decode_value = decode_func(value_type, namespace, encoding);
    if matches!(value_type.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) {
        write!(
            decode_value,
            " as {}",
            value_type.cs_type_string(namespace, TypeContext::Nested, false),
        );
    }

    // Use WithOptionalValueType method if encoding is not Slice1 and the value type is optional
    if encoding != Encoding::Slice1 && value_type.is_optional {
        format!(
            "\
decoder.DecodeDictionaryWithOptionalValueType(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
            dictionary_type = dictionary_ref.cs_type_string(namespace, TypeContext::Decode, true),
            decode_key = decode_key.indent(),
            decode_value = decode_value.indent(),
        )
    } else {
        format!(
            "\
decoder.DecodeDictionary(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
            dictionary_type = dictionary_ref.cs_type_string(namespace, TypeContext::Decode, true),
            decode_key = decode_key.indent(),
            decode_value = decode_value.indent(),
        )
    }
    .into()
}

pub fn decode_sequence(sequence_ref: &TypeRef<Sequence>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let element_type = &sequence_ref.element_type;

    let generic_attribute = sequence_ref.find_attribute(false, match_cs_generic);

    if generic_attribute.is_none() && matches!(element_type.concrete_type(), Types::Sequence(_)) {
        // For nested sequences we want to cast Foo[][] returned by DecodeSequence to IList<Foo>[]
        // used in the request and response decode methods.
        write!(
            code,
            "({}[])",
            element_type.cs_type_string(namespace, TypeContext::Nested, true),
        );
    };

    if generic_attribute.is_some() {
        let arg: Option<String> = match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.is_numeric_or_bool() && primitive.fixed_wire_size().is_some() => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than decoding the collection elements one by one.
                Some(format!(
                    "decoder.DecodeSequence<{}>()",
                    element_type.cs_type_string(namespace, TypeContext::Decode, true),
                ))
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than decoding the collection elements one by one.
                if enum_def.is_unchecked {
                    Some(format!(
                        "decoder.DecodeSequence<{}>()",
                        element_type.cs_type_string(namespace, TypeContext::Decode, true),
                    ))
                } else {
                    Some(format!(
                        "\
decoder.DecodeSequence(
    ({enum_type_name} e) => _ = {underlying_extensions_class}.As{name}(({underlying_type})e))",
                        enum_type_name = element_type.cs_type_string(namespace, TypeContext::Decode, false),
                        underlying_extensions_class = enum_def.escape_scoped_identifier_with_suffix(
                            &format!("{}Extensions", enum_def.get_underlying_cs_type().to_case(Case::Pascal)),
                            namespace,
                        ),
                        name = enum_def.cs_identifier(Some(Case::Pascal)),
                        underlying_type = enum_def.get_underlying_cs_type(),
                    ))
                }
            }
            _ => {
                if encoding != Encoding::Slice1 && element_type.is_optional {
                    write!(
                        code,
                        "\
decoder.DecodeSequenceOfOptionals(
    sequenceFactory: (size) => new {sequence_type}(size),
    {decode_func})",
                        sequence_type = sequence_ref.cs_type_string(namespace, TypeContext::Decode, true),
                        decode_func = decode_func(element_type, namespace, encoding).indent(),
                    );
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeSequence(
    sequenceFactory: (size) => new {sequence_type}(size),
    {decode_func})",
                        sequence_type = sequence_ref.cs_type_string(namespace, TypeContext::Decode, true),
                        decode_func = decode_func(element_type, namespace, encoding).indent(),
                    );
                }
                None
            }
        };

        if let Some(arg) = arg {
            write!(
                code,
                "\
new {}(
    {})",
                sequence_ref.cs_type_string(namespace, TypeContext::Decode, true),
                CodeBlock::from(arg).indent(),
            );
        }
    } else if encoding != Encoding::Slice1 && element_type.is_optional {
        write!(
            code,
            "\
decoder.DecodeSequenceOfOptionals(
    {})",
            decode_func(element_type, namespace, encoding).indent(),
        )
    } else {
        match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.fixed_wire_size().is_some() => {
                write!(
                    code,
                    "decoder.DecodeSequence<{}>()",
                    element_type.cs_type_string(namespace, TypeContext::Decode, true),
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() => {
                if enum_def.is_unchecked {
                    write!(
                        code,
                        "decoder.DecodeSequence<{}>()",
                        element_type.cs_type_string(namespace, TypeContext::Decode, true),
                    )
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeSequence(
    ({enum_type} e) => _ = {underlying_extensions_class}.As{name}(({underlying_type})e))",
                        enum_type = element_type.cs_type_string(namespace, TypeContext::Decode, false),
                        underlying_extensions_class = enum_def.escape_scoped_identifier_with_suffix(
                            &format!("{}Extensions", enum_def.get_underlying_cs_type().to_case(Case::Pascal)),
                            namespace,
                        ),
                        name = enum_def.cs_identifier(Some(Case::Pascal)),
                        underlying_type = enum_def.get_underlying_cs_type(),
                    );
                }
            }
            _ => {
                write!(
                    code,
                    "\
decoder.DecodeSequence(
    {})",
                    decode_func(element_type, namespace, encoding).indent(),
                )
            }
        }
    }

    code
}

fn decode_stream_parameter(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    if type_ref.is_optional {
        CodeBlock::from(format!(
            "\
(ref SliceDecoder decoder) =>
{{
    var bitSequenceReader = decoder.GetBitSequenceReader(1);
    if (bitSequenceReader.Read())
    {{
        return {decode_func_body};
    }}
    else
    {{
        return null;
    }}
}}
"
        ))
    } else {
        write!(code, "(ref SliceDecoder decoder) => {decode_func_body}");
    }
    code
}

pub fn decode_func(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    CodeBlock::from(format!("(ref SliceDecoder decoder) => {decode_func_body}"))
}

fn decode_func_body(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    // For value types the type declaration includes ? at the end, but the type name does not.
    let type_name = if type_ref.is_optional && type_ref.is_value_type() {
        type_ref.cs_type_string(namespace, TypeContext::Decode, true)
    } else {
        type_ref.cs_type_string(namespace, TypeContext::Decode, false)
    };

    let mut code: CodeBlock = match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if encoding == Encoding::Slice1 && type_ref.is_optional {
                format!("decoder.DecodeNullableProxy<{type_name}>()")
            } else {
                format!("decoder.DecodeProxy<{type_name}>()")
            }
        }
        _ if type_ref.is_class_type() => {
            // is_class_type is either Typeref::Class or Primitive::AnyClass
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                format!(
                    "decoder.DecodeNullableClass<{}>()",
                    type_ref.cs_type_string(namespace, TypeContext::Decode, true),
                )
            } else {
                format!("decoder.DecodeClass<{type_name}>()")
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            // Primitive::AnyClass is handled above by is_class_type branch
            if matches!(primitive_ref.definition(), Primitive::ServiceAddress)
                && encoding == Encoding::Slice1
                && type_ref.is_optional
            {
                "decoder.DecodeNullableServiceAddress()".to_owned()
            } else {
                format!("decoder.Decode{}()", primitive_ref.type_suffix(),)
            }
        }
        TypeRefs::Sequence(sequence_ref) => {
            format!("{}", decode_sequence(sequence_ref, namespace, encoding))
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            format!("{}", decode_dictionary(dictionary_ref, namespace, encoding))
        }
        TypeRefs::Enum(enum_ref) => {
            format!(
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Some(Case::Pascal)),
            )
        }
        TypeRefs::Struct(_) | TypeRefs::Exception(_) => {
            format!("new {type_name}(ref decoder)")
        }
        TypeRefs::CustomType(custom_type_ref) => {
            format!(
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = custom_type_ref.cs_identifier(Some(Case::Pascal)),
            )
        }
        TypeRefs::Class(_) => panic!("unexpected, see is_class_type above"),
    }
    .into();

    if type_ref.is_optional && type_ref.is_value_type() {
        write!(code, " as {type_name}?");
    }

    code
}

pub fn decode_operation(operation: &Operation, dispatch: bool) -> CodeBlock {
    let mut code = CodeBlock::default();

    let namespace = &operation.namespace();

    let non_streamed_members = if dispatch {
        operation.non_streamed_parameters()
    } else {
        operation.non_streamed_return_members()
    };

    assert!(!non_streamed_members.is_empty());

    let (required_members, tagged_members) = get_sorted_members(&non_streamed_members);

    let bit_sequence_size = get_bit_sequence_size(operation.encoding, &non_streamed_members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({bit_sequence_size});",
        );
    }

    for member in required_members {
        writeln!(
            code,
            "{param_type} {decode}",
            // For optional value types we have to use the full type as the compiler cannot
            // disambiguate between null and the actual value type.
            param_type = match member.data_type().is_optional && member.data_type().is_value_type() {
                true => member.data_type().cs_type_string(namespace, TypeContext::Decode, false),
                false => String::from("var"),
            },
            decode = decode_member(
                member,
                namespace,
                &member.parameter_name_with_prefix("sliceP_"),
                operation.encoding,
            ),
        )
    }

    for member in tagged_members {
        writeln!(
            code,
            "{param_type} {decode}",
            // For optional value types we have to use the full type as the compiler cannot
            // disambiguate between null and the actual value type.
            param_type = match member.data_type().is_value_type() {
                true => member.data_type().cs_type_string(namespace, TypeContext::Decode, false),
                false => String::from("var"),
            },
            decode = decode_tagged(
                member,
                namespace,
                &member.parameter_name_with_prefix("sliceP_"),
                false, // no tag end marker for operations
                operation.encoding
            ),
        )
    }

    writeln!(code, "return {};", non_streamed_members.to_argument_tuple("sliceP_"));

    code
}

pub fn decode_operation_stream(
    stream_member: &Parameter,
    namespace: &str,
    encoding: Encoding,
    dispatch: bool,
) -> CodeBlock {
    let cs_encoding = encoding.to_cs_encoding();
    let param_type = stream_member.data_type();
    let param_type_str = param_type.cs_type_string(namespace, TypeContext::Decode, false);
    let fixed_wire_size = param_type.fixed_wire_size();

    match param_type.concrete_type() {
        Types::Primitive(Primitive::UInt8) if !param_type.is_optional => {
            panic!("Must not be called for UInt8 parameters as there is no decoding");
        }
        _ => FunctionCallBuilder::new(&format!("payloadContinuation.ToAsyncEnumerable<{param_type_str}>"))
            .arguments_on_newline(true)
            .add_argument(cs_encoding)
            .add_argument(decode_stream_parameter(param_type, namespace, encoding).indent())
            .add_argument_if(fixed_wire_size.is_some(), || fixed_wire_size.unwrap().to_string())
            .add_argument_unless(dispatch || fixed_wire_size.is_some(), "sender")
            .add_argument_unless(
                fixed_wire_size.is_some(),
                "sliceFeature: request.Features.Get<IceRpc.Slice.ISliceFeature>()",
            )
            .build(),
    }
}
