//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#include <IceUtil/StringUtil.h>
#include <IceUtil/FileUtil.h>
#include <Gen.h>

#include <limits>
#ifndef _WIN32
#  include <unistd.h>
#else
#  include <direct.h>
#endif

#include <IceUtil/Iterator.h>
#include <IceUtil/UUID.h>
#include <Slice/FileTracker.h>
#include <Slice/Util.h>
#include <string.h>

using namespace std;
using namespace Slice;
using namespace IceUtil;
using namespace IceUtilInternal;

namespace
{

bool
isIdempotent(const OperationPtr& operation)
{
    // TODO: eliminate Nonmutating enumerator in the parser together with the nonmutating metadata.
    return operation->mode() != Operation::Normal;
}

bool
isDefaultInitialized(const MemberPtr& member)
{
    if (OptionalPtr::dynamicCast(member->type()))
    {
        return true;
    }

    auto st = StructPtr::dynamicCast(member->type());
    if (st)
    {
        for (auto m: st->dataMembers())
        {
            if (!isDefaultInitialized(m))
            {
                return false;
            }
        }
        return true;
    }

    return isValueType(member->type());
}

string
opFormatTypeToString(const OperationPtr& op)
{
    // TODO: eliminate DefaultFormat in the parser (DefaultFormat means the communicator default that was removed in
    // IceRPC)
    switch (op->format())
    {
        case DefaultFormat:
        case CompactFormat:
            return "default"; // same as Compact
        case SlicedFormat:
            return "IceRpc.Slice.FormatType.Sliced";
        default:
            assert(false);
    }

    return "???";
}

void
emitDeprecate(const ContainedPtr& p1, bool checkContainer, Output& out)
{
    string reason = getDeprecateReason(p1, checkContainer);
    if(!reason.empty())
    {
        out << nl << "[global::System.Obsolete(\"" << reason << "\")]";
    }
}

string
getEscapedParamName(const OperationPtr& p, const string& name)
{
    for (const auto& param : p->params())
    {
        if (param->name() == name)
        {
            return name + "_";
        }
    }
    return name;
}

string
getEscapedParamName(const ExceptionPtr& p, const string& name)
{
    for (const auto& member : p->allDataMembers())
    {
        if (member->name() == name)
        {
            return name + "_";
        }
    }
    return name;
}

}

Slice::CsVisitor::CsVisitor(Output& out) :
    _out(out)
{
}

Slice::CsVisitor::~CsVisitor()
{
}

void
Slice::CsVisitor::writeMarshal(const OperationPtr& operation, bool returnType)
{
    string ns = getNamespace(operation->interface());

    MemberList members = returnType ? operation->returnType() : operation->params();
    MemberPtr streamParam;
    if (members.back()->stream())
    {
        streamParam = members.back();
        members.pop_back();
    }
    auto [requiredMembers, taggedMembers] = getSortedMembers(members);

    int bitSequenceIndex = -1;
    size_t bitSequenceSize = returnType ? operation->returnBitSequenceSize() : operation->paramsBitSequenceSize();

    if (bitSequenceSize > 0)
    {
        _out << nl << "var bitSequence = encoder.EncodeBitSequence(" << bitSequenceSize << ");";
    }

    bool write11ReturnLast = returnType && operation->hasReturnAndOut() && !members.front()->tagged() &&
        requiredMembers.size() > 1;
    if (write11ReturnLast)
    {
        _out << nl << "if (encoder.Encoding != IceRpc.Encoding.Ice11)";
        _out << sb;
    }

    // Two loops when write11ReturnLast is true.
    for (int i = 0; i < (write11ReturnLast ? 2 : 1); i++)
    {
        if (bitSequenceSize > 0)
        {
            bitSequenceIndex = 0;
        }

        for (const auto& member : requiredMembers)
        {
            writeMarshalCode(_out,
                             member->type(),
                             bitSequenceIndex,
                             false,
                             ns,
                             members.size() == 1 ? "value" : "value." + fieldName(member));
        }

        if (bitSequenceSize > 0)
        {
            assert(static_cast<size_t>(bitSequenceIndex) == bitSequenceSize);
        }

        for (const auto& member : taggedMembers)
        {
            writeTaggedMarshalCode(_out,
                                   OptionalPtr::dynamicCast(member->type()),
                                   false,
                                   ns,
                                   members.size() == 1 ? "value" : "value." + fieldName(member), member->tag());
        }

        if (i == 0 && write11ReturnLast) // only for first loop
        {
            _out << eb;
            _out << nl;
            _out << "else";
            _out << sb;

            // Repeat after updating requiredMembers
            requiredMembers.push_back(requiredMembers.front());
            requiredMembers.pop_front();
        }
    }

    if (write11ReturnLast)
    {
        _out << eb;
    }
}

void
Slice::CsVisitor::writeUnmarshal(const OperationPtr& operation, bool returnType)
{
    string ns = getNamespace(operation->interface());

    MemberList members = returnType ? operation->returnType() : operation->params();
    MemberPtr streamParam;
    if (members.back()->stream())
    {
        streamParam = members.back();
        members.pop_back();
    }

    auto [requiredMembers, taggedMembers] = getSortedMembers(members);

    int bitSequenceIndex = -1;
    size_t bitSequenceSize = returnType ? operation->returnBitSequenceSize() : operation->paramsBitSequenceSize();

    if (bitSequenceSize > 0)
    {
        _out << nl << "var bitSequence = decoder.DecodeBitSequence(" << bitSequenceSize << ");";
    }

    bool read11ReturnLast = returnType && operation->hasReturnAndOut() && requiredMembers.size() > 1 &&
        !members.front()->tagged();

    if (read11ReturnLast)
    {
        _out << nl << "if (decoder.Encoding != IceRpc.Encoding.Ice11)";
        _out << sb;
    }

    // Two loops when write11ReturnLast is true.
    for (int i = 0; i < (read11ReturnLast ? 2 : 1); i++)
    {
        if (bitSequenceSize > 0)
        {
            bitSequenceIndex = 0;
        }

        for (const auto& member : requiredMembers)
        {
            _out << nl << paramTypeStr(member, ns, false);
            _out << " ";
            writeUnmarshalCode(_out, member->type(), bitSequenceIndex, ns, paramName(member, "iceP_"));
        }
        if (bitSequenceSize > 0)
        {
            assert(static_cast<size_t>(bitSequenceIndex) == bitSequenceSize);
        }

        for (const auto &member : taggedMembers)
        {
            _out << nl << paramTypeStr(member, ns, false) << " ";
            writeTaggedUnmarshalCode(_out,
                                     OptionalPtr::dynamicCast(member->type()),
                                     ns,
                                     paramName(member, "iceP_"),
                                     member->tag(),
                                     nullptr);
        }

        if (streamParam)
        {
            _out << nl << paramTypeStr(streamParam, ns, false) << " " << paramName(streamParam, "iceP_");

            if (auto builtin = BuiltinPtr::dynamicCast(streamParam->type());
                builtin && builtin->kind() == Builtin::KindByte)
            {
                if (returnType)
                {
                    _out << " = streamParamReceiver!.ToByteStream();";
                }
                else
                {
                    _out << " = IceRpc.Slice.StreamParamReceiver.ToByteStream(request);";
                }
            }
            else
            {
                if (returnType)
                {
                    _out << " = streamParamReceiver!.ToAsyncEnumerable<" << typeToString(streamParam->type(), ns) << ">(";
                    _out.inc();
                    _out << nl << "response,"
                        << nl << "invoker,"
                        << nl << "_defaultIceDecoderFactories,"
                        << nl << decodeFunc(streamParam->type(), ns) << ");";
                    _out.dec();
                }
                else
                {
                    _out << " = IceRpc.Slice.StreamParamReceiver.ToAsyncEnumerable<" << typeToString(streamParam->type(), ns)
                         << ">(";
                    _out.inc();
                    _out << nl << "request,"
                        << nl << "_defaultIceDecoderFactories,"
                        << nl << decodeFunc(streamParam->type(), ns) << ");";
                    _out.dec();
                }
            }
        }

        _out << nl << "return ";
        if (streamParam)
        {
            if (members.size() == 0)
            {
                _out << paramName(streamParam, "iceP_") << ";";
            }
            else
            {
                _out << spar << getNames(members, "iceP_") << paramName(streamParam, "iceP_") << epar << ";";
            }
        }
        else
        {
            if (members.size() == 1)
            {
                _out << paramName(members.front(), "iceP_") << ";";
            }
            else
            {
                _out << spar << getNames(members, "iceP_") << epar << ";";
            }
        }

        if (i == 0 && read11ReturnLast)
        {
            _out << eb;
            _out << nl;
            _out << "else";
            _out << sb;

            // Repeat after updating requiredMembers
            requiredMembers.push_back(requiredMembers.front());
            requiredMembers.pop_front();
        }
    }

    if (read11ReturnLast)
    {
        _out << eb;
    }
}

void
Slice::CsVisitor::writeMarshalDataMembers(const MemberList& p, const string& ns, unsigned int baseTypes)
{
#ifndef NDEBUG
    int currentTag = -1; // just to verify sortMembers sorts correctly
#endif

    auto [requiredMembers, taggedMembers] = getSortedMembers(p);
    int bitSequenceIndex = -1;
    // Tagged members are encoded in a dictionary and don't count towards the optional bit sequence size.
    size_t bitSequenceSize = getBitSequenceSize(requiredMembers);
    if (bitSequenceSize > 0)
    {
        _out << nl << "var bitSequence = encoder.EncodeBitSequence(" << bitSequenceSize << ");";
        bitSequenceIndex = 0;
    }

    for (const auto& member : requiredMembers)
    {
#ifndef NDEBUG
            assert(currentTag == -1);
#endif
            writeMarshalCode(_out, member->type(), bitSequenceIndex, true, ns,
                "this." + fixId(fieldName(member), baseTypes));
    }
    for (const auto& member : taggedMembers)
    {
#ifndef NDEBUG
            assert(member->tag() > currentTag);
            currentTag = member->tag();
#endif
            writeTaggedMarshalCode(_out, OptionalPtr::dynamicCast(member->type()), true, ns,
                "this." + fixId(fieldName(member), baseTypes), member->tag());
    }

    if (bitSequenceSize > 0)
    {
        assert(static_cast<size_t>(bitSequenceIndex) == bitSequenceSize);
    }
}

void
Slice::CsVisitor::writeUnmarshalDataMembers(const MemberList& p, const string& ns, unsigned int baseTypes)
{
    auto [requiredMembers, taggedMembers] = getSortedMembers(p);
    int bitSequenceIndex = -1;
    // Tagged members are encoded in a dictionary and don't count towards the optional bit sequence size.
    size_t bitSequenceSize = getBitSequenceSize(requiredMembers);
    if (bitSequenceSize > 0)
    {
        _out << nl << "var bitSequence = decoder.DecodeBitSequence(" << bitSequenceSize << ");";
        bitSequenceIndex = 0;
    }

    for (const auto& member : requiredMembers)
    {
        _out << nl;
        writeUnmarshalCode(_out, member->type(), bitSequenceIndex, ns,
            "this." + fixId(fieldName(member), baseTypes));
    }
    for (const auto& member : taggedMembers)
    {
        _out << nl;
        writeTaggedUnmarshalCode(_out, OptionalPtr::dynamicCast(member->type()), ns,
            "this." + fixId(fieldName(member), baseTypes), member->tag(), member);
    }

    if (bitSequenceSize > 0)
    {
        assert(static_cast<size_t>(bitSequenceIndex) == bitSequenceSize);
    }
}

string
getParamAttributes(const MemberPtr& p)
{
    string result;
    for(const auto& s : p->getAllMetadata())
    {
        static const string prefix = "cs:attribute:";
        if(s.find(prefix) == 0)
        {
            result += "[" + s.substr(prefix.size()) + "] ";
        }
    }
    return result;
}

vector<string>
getInvocationParams(const OperationPtr& op, const string& ns, bool defaultValues, const string& prefix = "")
{
    vector<string> params;
    for (const auto& p : op->params())
    {
        ostringstream param;
        param << getParamAttributes(p);
        param << CsGenerator::typeToString(p->type(), ns, true, true, p->stream()) << " " << paramName(p, prefix);
        params.push_back(param.str());
    }

    string invocation = prefix.empty() ? getEscapedParamName(op, "invocation") : "invocation";
    string cancel = prefix.empty() ? getEscapedParamName(op, "cancel") : "cancel";

    if (defaultValues)
    {
        params.push_back("IceRpc.Invocation? " + invocation + " = null");
        params.push_back("global::System.Threading.CancellationToken " + cancel + " = default");
    }
    else
    {
        params.push_back("IceRpc.Invocation? " + invocation);
        params.push_back("global::System.Threading.CancellationToken " + cancel);
    }
    return params;
}

void
Slice::CsVisitor::emitCommonAttributes()
{
   // _out << nl << "[global::System.CodeDom.Compiler.GeneratedCode(\"slice2cs\", \"" << ICE_STRING_VERSION << "\")]";
}

void
Slice::CsVisitor::emitEditorBrowsableNeverAttribute()
{
    _out << nl << "[global::System.ComponentModel.EditorBrowsable("
         << "global::System.ComponentModel.EditorBrowsableState.Never)]";
}

void
Slice::CsVisitor::emitEqualityOperators(const string& name)
{
    _out << sp;
    _out << nl << "/// <summary>The equality operator == returns true if its operands are equal, false otherwise."
         << "</summary>";
    _out << nl << "/// <param name=\"lhs\">The left hand side operand.</param>";
    _out << nl << "/// <param name=\"rhs\">The right hand side operand.</param>";
    _out << nl << "/// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>";
    _out << nl << "public static bool operator ==(" << name << " lhs, " << name << " rhs)";
    _out << " => lhs.Equals(rhs);";

    _out << sp;
    _out << nl << "/// <summary>The inequality operator != returns true if its operands are not equal, false otherwise."
         << "</summary>";
    _out << nl << "/// <param name=\"lhs\">The left hand side operand.</param>";
    _out << nl << "/// <param name=\"rhs\">The right hand side operand.</param>";
    _out << nl << "/// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>";
    _out << nl << "public static bool operator !=(" << name << " lhs, " << name << " rhs)";
    _out << " => !lhs.Equals(rhs);";
}

void
Slice::CsVisitor::emitCustomAttributes(const ContainedPtr& p)
{
    StringList metadata = p->getAllMetadata();
    for(StringList::const_iterator i = metadata.begin(); i != metadata.end(); ++i)
    {
        static const string prefix = "cs:attribute:";
        if(i->find(prefix) == 0)
        {
            _out << nl << '[' << i->substr(prefix.size()) << ']';
        }
    }
}

void
Slice::CsVisitor::emitCompactTypeIdAttribute(int compactTypeId)
{
    _out << nl << "[IceRpc.Slice.CompactTypeId(" << compactTypeId << ")]";
}

void
Slice::CsVisitor::emitTypeIdAttribute(const string& typeId)
{
    _out << nl << "[IceRpc.Slice.TypeId(\"" << typeId << "\")]";
}

string
Slice::CsVisitor::writeValue(const TypePtr& type, const string& ns)
{
    assert(type);

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    if(builtin)
    {
        switch(builtin->kind())
        {
            case Builtin::KindBool:
            {
                return "false";
            }
            case Builtin::KindByte:
            case Builtin::KindShort:
            case Builtin::KindUShort:
            case Builtin::KindInt:
            case Builtin::KindUInt:
            case Builtin::KindVarInt:
            case Builtin::KindVarUInt:
            case Builtin::KindLong:
            case Builtin::KindULong:
            case Builtin::KindVarLong:
            case Builtin::KindVarULong:
            {
                return "0";
            }
            case Builtin::KindFloat:
            {
                return "0.0f";
            }
            case Builtin::KindDouble:
            {
                return "0.0";
            }
            default:
            {
                return "null";
            }
        }
    }

    EnumPtr en = EnumPtr::dynamicCast(type);
    if(en)
    {
        return typeToString(type, ns) + "." + fixId((*en->enumerators().begin())->name());
    }

    StructPtr st = StructPtr::dynamicCast(type);
    if(st)
    {
        return "new " + typeToString(type, ns) + "()";
    }

    return "null";
}

void
Slice::CsVisitor::writeSuppressNonNullableWarnings(const MemberList& members, unsigned int baseTypes)
{
    // This helper function is called only for class/exception data members.

    for (const auto& p: members)
    {
        TypePtr memberType = p->type();
        if (!OptionalPtr::dynamicCast(memberType))
        {
            BuiltinPtr builtin = BuiltinPtr::dynamicCast(memberType);

            if (memberType->isClassType() ||
                memberType->isInterfaceType() ||
                SequencePtr::dynamicCast(memberType) ||
                DictionaryPtr::dynamicCast(memberType) ||
                (builtin && builtin->kind() == Builtin::KindString))
            {
                // This is to suppress compiler warnings for non-nullable fields.
                _out << nl << "this." << fixId(fieldName(p), baseTypes) << " = null!;";
            }
        }
    }
}

namespace
{

//
// Convert the identifier part of a Java doc Link tag to a CSharp identifier. If the identifier
// is an interface the link should point to the corresponding generated proxy, we apply the
// case conversions required to match the C# generated code.
//
string
csharpIdentifier(const ContainedPtr& contained, const string& identifier)
{
    string ns = getNamespace(contained);
    string typeName;
    string memberName;
    string::size_type pos = identifier.find('#');
    if(pos == 0)
    {
        memberName = identifier.substr(1);
    }
    else if(pos == string::npos)
    {
        typeName = identifier;
    }
    else
    {
        typeName = identifier.substr(0, pos);
        memberName = identifier.substr(pos + 1);
    }

    // lookup the Slice definition for the identifier
    ContainedPtr definition;
    if(typeName.empty())
    {
        definition = contained;
    }
    else
    {
        TypeList types = contained->unit()->lookupTypeNoBuiltin(typeName, false, true);

        if (types.empty())
        {
            definition = nullptr;
        }
        else
        {
            TypePtr type = types.front();
            if (auto typealias = TypeAliasPtr::dynamicCast(type))
            {
                type = typealias->underlying();
            }
            definition = ContainedPtr::dynamicCast(type);
        }
    }

    ostringstream os;
    if(!definition || !normalizeCase(definition))
    {
        if(typeName == "::Ice::Object")
        {
            os << "Ice.IServicePrx";
        }
        else
        {
            os << fixId(typeName);
        }
    }
    else
    {
        InterfaceDefPtr def = InterfaceDefPtr::dynamicCast(definition);
        if(!def)
        {
            InterfaceDeclPtr decl = InterfaceDeclPtr::dynamicCast(definition);
            if(decl)
            {
                def = decl->definition();
            }
        }

        if(def)
        {
            os << getUnqualified(fixId(definition->scope()) + interfaceName(def), ns) << "Prx";
        }
        else
        {
            typeName = fixId(typeName);
            pos = typeName.rfind(".");
            if(pos == string::npos)
            {
                os << pascalCase(fixId(typeName));
            }
            else
            {
                os << typeName.substr(0, pos) << "." << pascalCase(typeName.substr(pos + 1));
            }
        }
    }

    if(!memberName.empty())
    {
        os << "." << (definition && normalizeCase(definition) ? pascalCase(fixId(memberName)) : fixId(memberName));
    }
    string result = os.str();
    //
    // strip global:: prefix if present, it is not supported in doc comment cref attributes
    //
    const string global = "global::";
    if(result.find(global) == 0)
    {
        result = result.substr(global.size());
    }
    return result;
}

vector<string>
splitLines(const string& s)
{
    vector<string> lines;
    istringstream is(s);
    for(string line; getline(is, line, '\n');)
    {
        lines.push_back(trim(line));
    }
    return lines;
}

//
// Transform a Java doc style tag to a C# doc style tag, returns a map indexed by the C#
// tag name attribute and the value contains all the lines in the comment.
//
// @param foo is the Foo argument -> {"foo": ["foo is the Foo argument"]}
//
map<string, vector<string>>
processTag(const string& sourceTag, const string& s)
{
    map<string, vector<string>> result;
    for(string::size_type pos = s.find(sourceTag); pos != string::npos; pos = s.find(sourceTag, pos + 1))
    {
        string::size_type startIdent = s.find_first_not_of(" \t", pos + sourceTag.size());
        string::size_type endIdent = s.find_first_of(" \t", startIdent);
        string::size_type endComment = s.find_first_of("@", endIdent);
        if(endIdent != string::npos)
        {
            string ident = s.substr(startIdent, endIdent - startIdent);
            string comment = s.substr(endIdent + 1,
                                      endComment == string::npos ? endComment : endComment - endIdent - 1);
            result[ident] = splitLines(trim(comment));
        }
    }
    return result;
}

CommentInfo
processComment(const ContainedPtr& contained, const string& deprecateReason)
{
    //
    // Strip HTML markup and javadoc links that are not displayed by Visual Studio.
    //
    string data = contained->comment();
    for(string::size_type pos = data.find('<'); pos != string::npos; pos = data.find('<', pos))
    {
        string::size_type endpos = data.find('>', pos);
        if(endpos == string::npos)
        {
            break;
        }
        data.erase(pos, endpos - pos + 1);
    }

    const string link = "{@link ";
    for(string::size_type pos = data.find(link); pos != string::npos; pos = data.find(link, pos))
    {
        data.erase(pos, link.size());
        string::size_type endpos = data.find('}', pos);
        if(endpos != string::npos)
        {
            string ident = data.substr(pos, endpos - pos);
            data.erase(pos, endpos - pos + 1);
            data.insert(pos, csharpIdentifier(contained, ident));
        }
    }

    const string see = "{@see ";
    for(string::size_type pos = data.find(see); pos != string::npos; pos = data.find(see, pos))
    {
        string::size_type endpos = data.find('}', pos);
        if(endpos != string::npos)
        {
            string ident = data.substr(pos + see.size(), endpos - pos - see.size());
            data.erase(pos, endpos - pos + 1);
            data.insert(pos, "<see cref=\"" + csharpIdentifier(contained, ident) + "\"/>");
        }
    }

    CommentInfo comment;

    const string paramTag = "@param";
    const string throwsTag = "@throws";
    const string exceptionTag = "@exception";
    const string returnTag = "@return";

    string::size_type pos;
    for(pos = data.find('@'); pos != string::npos; pos = data.find('@', pos + 1))
    {
        if((data.substr(pos, paramTag.size()) == paramTag ||
            data.substr(pos, throwsTag.size()) == throwsTag ||
            data.substr(pos, exceptionTag.size()) == exceptionTag ||
            data.substr(pos, returnTag.size()) == returnTag))
        {
            break;
        }
    }

    if(pos > 0)
    {
        ostringstream os;
        os << trim(data.substr(0, pos));
        if(!deprecateReason.empty())
        {
            os << "<para>" << deprecateReason << "</para>";
        }
        comment.summaryLines = splitLines(os.str());
    }

    if(comment.summaryLines.empty() && !deprecateReason.empty())
    {
        comment.summaryLines.push_back(deprecateReason);
    }

    comment.params = processTag("@param", data);
    comment.exceptions = processTag("@throws", data);

    pos = data.find(returnTag);
    if(pos != string::npos)
    {
        pos += returnTag.size();
        string::size_type endComment = data.find("@", pos);
        comment.returnLines = splitLines(
            trim(data.substr(pos , endComment == string::npos ? endComment : endComment - pos)));
    }

    return comment;
}

}

void writeDocCommentLines(IceUtilInternal::Output& out, const vector<string>& lines)
{
    for(vector<string>::const_iterator i = lines.begin(); i != lines.end(); ++i)
    {
        if(i == lines.begin())
        {
            out << *i;
        }
        else
        {
            out << nl << "///";
            if(!i->empty())
            {
                out << " " << (*i);
            }
        }
    }
}

void writeDocCommentLines(IceUtilInternal::Output& out,
                          const vector<string>& lines,
                          const string& tag,
                          const string& name = "",
                          const string& value = "")
{
    if (!lines.empty())
    {
        out << nl << "/// <" << tag;
        if (!name.empty())
        {
            out << " " << name << "=\"" << value << "\"";
        }
        out << ">";
        writeDocCommentLines(out, lines);
        out << "</" << tag << ">";
    }
}

void
Slice::CsVisitor::writeTypeDocComment(const ContainedPtr& p, const string& deprecateReason)
{
    CommentInfo comment = processComment(p, deprecateReason);
    writeDocCommentLines(_out, comment.summaryLines, "summary");
}

void
Slice::CsVisitor::writeProxyDocComment(const InterfaceDefPtr& p, const std::string& deprecatedReason)
{
    CommentInfo comment = processComment(p, deprecatedReason);
    comment.summaryLines.insert(comment.summaryLines.cbegin(),
        "The client-side interface for Slice interface " + p->name() + ".");
    comment.summaryLines.push_back("<seealso cref=\"" + fixId(interfaceName(p)) + "\"/>.");
    writeDocCommentLines(_out, comment.summaryLines, "summary");
}

void
Slice::CsVisitor::writeServantDocComment(const InterfaceDefPtr& p, const std::string& deprecatedReason)
{
    CommentInfo comment = processComment(p, deprecatedReason);
    comment.summaryLines.insert(comment.summaryLines.cbegin(),
        "Interface used to implement services for Slice interface " + p->name() + ".");
    comment.summaryLines.push_back("<seealso cref=\"" + interfaceName(p) + "Prx\"/>.");
    writeDocCommentLines(_out, comment.summaryLines, "summary");
}

void
Slice::CsVisitor::writeOperationDocComment(const OperationPtr& p, const string& deprecateReason, bool dispatch)
{
    CommentInfo comment = processComment(p, deprecateReason);
    writeDocCommentLines(_out, comment.summaryLines, "summary");
    writeParamDocComment(p, comment, InParam);

    auto returnType = p->returnType();

    if(dispatch)
    {
        _out << nl << "/// <param name=\"" << getEscapedParamName(p, "dispatch")
             << "\">The dispatch properties.</param>";
    }
    else
    {
        _out << nl << "/// <param name=\"" << getEscapedParamName(p, "invocation")
            << "\">The invocation properties.</param>";
    }
    _out << nl << "/// <param name=\"" << getEscapedParamName(p, "cancel")
         << "\">A cancellation token that receives the cancellation requests.</param>";

    if(dispatch && p->hasMarshaledResult())
    {
        _out << nl << "/// <returns>The operation marshaled result.</returns>";
    }
    else if(returnType.size() == 1)
    {
        writeDocCommentLines(_out, comment.returnLines, "returns");
    }
    else if(returnType.size() > 1)
    {
        _out << nl << "/// <returns>Named tuple with the following fields:";

        for(const auto& param : returnType)
        {
            string name = paramName(param);
            if (name == "ReturnValue" && !comment.returnLines.empty())
            {
                _out << nl << "/// <para> " << name << ": ";
                writeDocCommentLines(_out, comment.returnLines);
                _out << "</para>";
            }
            else
            {
                auto i = comment.params.find(name);
                if(i != comment.params.end())
                {
                    _out << nl << "/// <para> " << name << ": ";
                    writeDocCommentLines(_out, i->second);
                    _out << "</para>";
                }
            }
        }
        _out << "</returns>";
    }

    for(const auto& e : comment.exceptions)
    {
        writeDocCommentLines(_out, e.second, "exceptions", "cref", e.first);
    }
}

void
Slice::CsVisitor::writeParamDocComment(const OperationPtr& op, const CommentInfo& comment, ParamDir paramType)
{
    // Collect the names of the in- or -out parameters to be documented.
    MemberList parameters = (paramType == InParam) ? op->params() : op->outParameters();
    for (const auto& param : parameters)
    {
        auto i = comment.params.find(param->name());
        if(i != comment.params.end())
        {
            writeDocCommentLines(_out, i->second, "param", "name", paramName(param, "", false));
        }
    }
}

void
Slice::CsVisitor::openNamespace(const ModulePtr& p, string prefix)
{
    // Prefix is used for the class and exception factories namespaces.
    // If prefix is not empty we purposefully ignore any namespace metadata.

    // prefix and _namespaceStack can not both be non-empty
    assert((prefix.empty() && _namespaceStack.empty()) || (prefix.empty() ^ _namespaceStack.empty()));

    string ns;
    if (prefix.empty())
    {
        // _namespaceStack will only be empty when we're at the the top level nested module
        string lastNamespace = _namespaceStack.empty() ? "" : _namespaceStack.top();
        string namespaceMetadata = getNamespaceMetadata(p);
        bool topLevelWithMetadata = _namespaceStack.empty() && !namespaceMetadata.empty();

        if (!lastNamespace.empty())
        {
            lastNamespace += ".";
        }

        // Use of the cs:namespace metadata consumes the module name
        ns = topLevelWithMetadata ? namespaceMetadata : lastNamespace + fixId(p->name());
    }
    else
    {
        // If prefix was not empty than p must be a top level module
        // Do not include any cs:namespace metadata
        ns = prefix + "." + fixId(p->name());
    }

    if(p->hasOnlySubModules())
    {
        _namespaceStack.push(ns);
    }
    else
    {
        _out << sp;
        _out << nl << "namespace " << ns << sb;
        _namespaceStack.push("");
    }
}

void
Slice::CsVisitor::closeNamespace()
{
    if (_namespaceStack.top().empty())
    {
        _out << eb;
    }
    _namespaceStack.pop();
}

Slice::Gen::Gen(const string& base, const vector<string>& includePaths, const string& dir) :
    _includePaths(includePaths)
{
    string fileBase = base;
    string::size_type pos = base.find_last_of("/\\");
    if(pos != string::npos)
    {
        fileBase = base.substr(pos + 1);
    }
    string file = fileBase + ".cs";

    if(!dir.empty())
    {
        file = dir + '/' + file;
    }

    _out.open(file.c_str());
    if(!_out)
    {
        ostringstream os;
        os << "cannot open '" << file << "': " << IceUtilInternal::errorToString(errno);
        throw FileException(__FILE__, __LINE__, os.str());
    }
    FileTracker::instance()->addFile(file);

    _out << "// Copyright (c) ZeroC, Inc. All rights reserved.";
    _out << nl;
    _out << nl << "// <auto-generated/>";
    _out << nl << "// IceRPC version: '" << ICE_STRING_VERSION << "'";
    _out << nl << "// Generated from file: '" << fileBase << ".ice'";
    _out << nl;
    _out << nl << "#nullable enable";
    _out << nl;
    _out << nl << "#pragma warning disable 1591 // Missing XML Comment";
    _out << nl << "using IceRpc.Slice;";
    _out << nl;
    _out << nl << "[assembly:IceRpc.Slice.Slice(\"" << fileBase << ".ice\")]";
}

Slice::Gen::~Gen()
{
    if(_out.isOpen())
    {
        _out << '\n';
    }
}

void
Slice::Gen::generate(const UnitPtr& p)
{
    CsGenerator::validateMetadata(p);

    UnitVisitor unitVisitor(_out);
    p->visit(&unitVisitor, false);

    TypesVisitor typesVisitor(_out);
    p->visit(&typesVisitor, false);

    ProxyVisitor proxyVisitor(_out);
    p->visit(&proxyVisitor, false);

    DispatcherVisitor dispatcherVisitor(_out);
    p->visit(&dispatcherVisitor, false);
}

void
Slice::Gen::closeOutput()
{
    _out.close();
}

Slice::Gen::UnitVisitor::UnitVisitor(IceUtilInternal::Output& out) :
    CsVisitor(out)
{
}

bool
Slice::Gen::UnitVisitor::visitUnitStart(const UnitPtr& p)
{
    DefinitionContextPtr dc = p->findDefinitionContext(p->topLevelFile());
    assert(dc);
    StringList globalMetadata = dc->getAllMetadata();

    static const string attributePrefix = "cs:attribute:";

    bool sep = false;
    for(StringList::const_iterator q = globalMetadata.begin(); q != globalMetadata.end(); ++q)
    {
        string::size_type pos = q->find(attributePrefix);
        if(pos == 0 && q->size() > attributePrefix.size())
        {
            if(!sep)
            {
                _out << sp;
                sep = true;
            }
            string attrib = q->substr(pos + attributePrefix.size());
            _out << nl << '[' << attrib << ']';
        }
    }
    return false;
}

Slice::Gen::TypesVisitor::TypesVisitor(IceUtilInternal::Output& out) :
    CsVisitor(out)
{
}

bool
Slice::Gen::TypesVisitor::visitModuleStart(const ModulePtr& p)
{
    if (p->hasClassDefs() || p->hasEnums() || p->hasExceptions() || p->hasStructs())
    {
        openNamespace(p);
        return true;
    }
    else
    {
        // don't generate a file with an empty namespace
        return false;
    }
}

void
Slice::Gen::TypesVisitor::visitModuleEnd(const ModulePtr&)
{
    closeNamespace();
}

bool
Slice::Gen::TypesVisitor::visitClassDefStart(const ClassDefPtr& p)
{
    string name = p->name();
    string scoped = fixId(p->scoped(), Slice::ObjectType);
    string ns = getNamespace(p);
    _out << sp;
    writeTypeDocComment(p, getDeprecateReason(p));

    emitCommonAttributes();
    emitTypeIdAttribute(p->scoped());
    if (p->compactId() >= 0)
    {
        emitCompactTypeIdAttribute(p->compactId());
    }
    emitCustomAttributes(p);
    _out << nl << "public partial class " << fixId(name) << " : "
         << (p->base() ? getUnqualified(p->base(), ns) : "IceRpc.AnyClass")
         << sb;
    return true;
}

void
Slice::Gen::TypesVisitor::visitClassDefEnd(const ClassDefPtr& p)
{
    string name = fixId(p->name());
    string ns = getNamespace(p);
    MemberList dataMembers = p->dataMembers();
    MemberList allDataMembers = p->allDataMembers();
    bool hasBaseClass = p->base();

    _out << sp;
    emitEditorBrowsableNeverAttribute();
    _out << nl << "public static " << (hasBaseClass ? "new " : "")
        << " readonly string IceTypeId = typeof(" << name << ").GetIceTypeId()!;";

    if (p->compactId() >= 0)
    {
        _out << sp;
        _out << nl << "private static readonly int _compactTypeId = typeof(" << name
            << ").GetIceCompactTypeId()!.Value;";
    }

    if (allDataMembers.empty())
    {
        // There is always at least another constructor, so we need to generate the parameterless constructor.
        _out << sp;
        _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";
        _out << nl << "public " << name << spar << epar;
        _out << sb;
        _out << eb;
    }
    else
    {
        // "One-shot" constructor
        _out << sp;
        _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";
        for (const auto& member : allDataMembers)
        {
            CommentInfo comment = processComment(member, "");
            writeDocCommentLines(_out, comment.summaryLines, "param", "name", paramName(member, "", false));
        }
        _out << nl << "public " << name
             << spar
             << mapfn<MemberPtr>(allDataMembers,
                                 [&ns](const auto& i)
                                 {
                                     return typeToString(i->type(), ns) + " " + fixId(i->name());
                                 })
             << epar;
        if (hasBaseClass && allDataMembers.size() != dataMembers.size())
        {
            _out.inc();
            _out << nl << ": base" << spar;
            vector<string> baseParamNames;
            for (const auto& d : p->base()->allDataMembers())
            {
                baseParamNames.push_back(fixId(d->name()));
            }
            _out << baseParamNames << epar;
            _out.dec();
        } // else we call implicitly the parameterless constructor of the base class (if there is a base class).

        _out << sb;
        for (const auto& d: dataMembers)
        {
            _out << nl << "this." << fixId(fieldName(d), Slice::ObjectType) << " = "
                 << fixId(d->name(), Slice::ObjectType) << ";";
        }
        _out << eb;

        // Second public constructor for all data members minus those with a default initializer. Can be parameterless.
        MemberList allMandatoryDataMembers;
        for (const auto& member: allDataMembers)
        {
            if (!isDefaultInitialized(member))
            {
                allMandatoryDataMembers.push_back(member);
            }
        }

        if (allMandatoryDataMembers.size() < allDataMembers.size()) // else, it's identical to the first ctor.
        {
            _out << sp;
            _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";
            for (const auto& member : allMandatoryDataMembers)
            {
                CommentInfo comment = processComment(member, "");
                writeDocCommentLines(_out, comment.summaryLines, "param", "name", paramName(member, "", false));
            }
            _out << nl << "public " << name
                << spar
                << mapfn<MemberPtr>(allMandatoryDataMembers,
                                    [&ns](const auto &i) {
                                         return typeToString(i->type(), ns) + " " + fixId(i->name());
                                    })
                << epar;
            if (hasBaseClass)
            {
                vector<string> baseParamNames;
                for (const auto& d: p->base()->allDataMembers())
                {
                    if (!isDefaultInitialized(d))
                    {
                        baseParamNames.push_back(fixId(d->name()));
                    }
                }
                if (!baseParamNames.empty())
                {
                    _out.inc();
                    _out << nl << ": base" << spar << baseParamNames << epar;
                    _out.dec();
                }
                // else we call implicitly the parameterless constructor of the base class.
            }
            _out << sb;
            for (const auto& d : dataMembers)
            {
                if (!isDefaultInitialized(d))
                {
                    _out << nl << "this." << fixId(fieldName(d), Slice::ObjectType) << " = "
                         << fixId(d->name(), Slice::ObjectType) << ";";
                }
            }
            _out << eb;
        }
    }

    // public constructor used for unmarshaling (always generated).
    // the factory parameter is used to distinguish this ctor from the parameterless ctor that users may want to add to
    // the partial class; it's not used otherwise.
    _out << sp;
    if (!hasBaseClass)
    {
        _out << nl << "[global::System.Diagnostics.CodeAnalysis.SuppressMessage(";
        _out.inc();
        _out << nl << "\"Microsoft.Performance\","
            << nl << "\"CA1801: Review unused parameters\","
            << nl << "Justification=\"Special constructor used for Ice unmarshaling\")]";
        _out.dec();
    }
    _out << nl << "/// <inherit-doc/>";
    emitEditorBrowsableNeverAttribute();
    _out << nl << "public " << name << "(IceDecoder? decoder)";
    if (hasBaseClass)
    {
        // We call the base class constructor to initialize the base class fields.
        _out.inc();
        _out << nl << ": base(decoder)";
        _out.dec();
    }
    _out << sb;
    writeSuppressNonNullableWarnings(dataMembers, ObjectType);
    _out << eb;

    writeMarshaling(p);
    _out << eb;
}

void
Slice::Gen::TypesVisitor::writeMarshaling(const ClassDefPtr& p)
{
    string name = fixId(p->name());
    string scoped = p->scoped();
    string ns = getNamespace(p);
    ClassList allBases = p->allBases();

    // Marshaling support
    MemberList members = p->dataMembers();
    const bool basePreserved = p->inheritsMetadata("preserve-slice");
    const bool preserved = p->hasMetadata("preserve-slice");

    ClassDefPtr base = p->base();

    if(preserved && !basePreserved)
    {
        _out << sp;
        _out << nl << "protected override global::System.Collections.Immutable.ImmutableList<IceRpc.Slice.SliceInfo> "
            << "IceUnknownSlices { get; set; } = "
            << "global::System.Collections.Immutable.ImmutableList<IceRpc.Slice.SliceInfo>.Empty;";
    }

    _out << sp;
    _out << nl << "protected override void IceEncode(Ice11Encoder encoder)";
    _out << sb;
    _out << nl << "encoder.IceStartSlice(IceTypeId";
    if (p->compactId() >= 0)
    {
        _out << ", _compactTypeId";
    }
    _out << ");";

    writeMarshalDataMembers(members, ns, 0);

    if(base)
    {
        _out << nl << "encoder.IceEndSlice(false);";
        _out << nl << "base.IceEncode(encoder);";
    }
    else
    {
         _out << nl << "encoder.IceEndSlice(true);"; // last slice
    }
    _out << eb;

    _out << sp;

    _out << nl << "protected override void IceDecode(Ice11Decoder decoder)";
    _out << sb;
    _out << nl << "decoder.IceStartSlice();";
    writeUnmarshalDataMembers(members, ns, 0);
    _out << nl << "decoder.IceEndSlice();";
    if (base)
    {
        _out << nl << "base.IceDecode(decoder);";
    }
    _out << eb;
}

bool
Slice::Gen::TypesVisitor::visitExceptionStart(const ExceptionPtr& p)
{
    string name = fixId(p->name());
    string ns = getNamespace(p);
    ExceptionPtr base = p->base();

    _out << sp;
    writeTypeDocComment(p, getDeprecateReason(p));
    emitDeprecate(p, false, _out);

    emitCommonAttributes();
    emitTypeIdAttribute(p->scoped());
    emitCustomAttributes(p);
    _out << nl << "public partial class " << name << " : ";
    if(base)
    {
        _out << getUnqualified(base, ns);
    }
    else
    {
        _out << "IceRpc.RemoteException";
    }
    _out << sb;
    return true;
}

void
Slice::Gen::TypesVisitor::visitExceptionEnd(const ExceptionPtr& p)
{
    string name = fixId(p->name());
    string ns = getNamespace(p);
    MemberList allDataMembers = p->allDataMembers();
    MemberList dataMembers = p->dataMembers();

    string messageParamName = getEscapedParamName(p, "message");
    string innerExceptionParamName = getEscapedParamName(p, "innerException");
    string retryPolicyParamName = getEscapedParamName(p, "retryPolicy");

    bool hasPublicParameterlessCtor = true;
    vector<string> allParameters;
    for (const auto& member : allDataMembers)
    {
        string memberName = fixId(member->name());
        string memberType = typeToString(member->type(), ns);
        allParameters.push_back(memberType + " " + memberName);

        if (hasPublicParameterlessCtor)
        {
            hasPublicParameterlessCtor = isDefaultInitialized(member);
        }
    }

    vector<string> baseParamNames;
    if (p->base())
    {
        for (const auto& member : p->base()->allDataMembers())
        {
            baseParamNames.push_back(fixId(member->name()));
        }
    }

    ExceptionPtr base = p->base();

    _out << nl << "private static readonly string _iceTypeId = typeof(" << name << ").GetIceTypeId()!;";

    // Up to 2 "one-shot" constructors
    for (int i = 0; i < 2; i++)
    {
        if (allParameters.size() > 0)
        {
            if (i == 0)
            {
                // Add retryPolicy last
                allParameters.push_back("IceRpc.RetryPolicy " + retryPolicyParamName + " = default");
                baseParamNames.push_back(retryPolicyParamName);
            }
            _out << sp;
            _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";

            if (i > 0)
            {
                _out << nl << "/// <param name=\"" << messageParamName
                     << "\">Message that describes the exception.</param>";
            }
            for (const auto& member : p->allDataMembers())
            {
                CommentInfo comment = processComment(member, "");
                writeDocCommentLines(_out, comment.summaryLines, "param", "name", paramName(member, "", false));
            }

            if (i > 0)
            {
                _out << nl << "/// <param name=\"" << innerExceptionParamName
                     << "\">The exception that is the cause of the current exception.</param>";
            }
            _out << nl << "/// <param name=\"" << retryPolicyParamName
                 << "\">The retry policy for the exception.</param>";
            _out << nl << "public " << name << spar << allParameters << epar;
            _out.inc();
            if (baseParamNames.size() > 0)
            {
                _out << nl << ": base" << spar << baseParamNames << epar;
            }
            // else we use the base's parameterless ctor.
            _out.dec();
            _out << sb;
            for (const auto& member : dataMembers)
            {
                string memberName = fixId(fieldName(member), Slice::ExceptionType);
                _out << nl << "this." << memberName << " = " << fixId(member->name()) << ';';
            }
            _out << eb;
        }

        if (i == 0)
        {
            if (allParameters.size() > 0)
            {
                allParameters.erase(prev(allParameters.end()));
                baseParamNames.erase(prev(baseParamNames.end()));
            }
            // Insert message first
            allParameters.insert(allParameters.cbegin(), "string? " + messageParamName);
            baseParamNames.insert(baseParamNames.cbegin(), messageParamName);

            // Add innerException and retryPolicy last
            allParameters.push_back("global::System.Exception? " + innerExceptionParamName + " = null");
            baseParamNames.push_back(innerExceptionParamName);

            allParameters.push_back("IceRpc.RetryPolicy " + retryPolicyParamName + " = default");
            baseParamNames.push_back(retryPolicyParamName);
        }
    }

    // public parameterless constructor (not always generated, see class comment)
    if (hasPublicParameterlessCtor)
    {
        _out << sp;
        _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";
        _out << nl << "/// <param name=\"" << retryPolicyParamName << "\">The retry policy for the exception.</param>";
        _out << nl << "public " << name << "(IceRpc.RetryPolicy retryPolicy = default)";
        _out.inc();
        _out << nl << ": base(retryPolicy)";
        _out.dec();
        _out << sb;
        _out << eb;
    }

    // public constructor used for Ice 1.1 decoding (always generated).
    _out << sp;
    _out << nl << "/// <inherit-doc/>";
    emitEditorBrowsableNeverAttribute();
    _out << nl << "public " << name << "(Ice11Decoder decoder)";
    _out.inc();
    _out << nl << ": base(decoder)";
    _out.dec();
    _out << sb;
    writeSuppressNonNullableWarnings(dataMembers, Slice::ExceptionType);
    _out << eb;

    if (!base)
    {
        // public constructor used for Ice 2.0 decoding
        _out << sp;
        _out << nl << "/// <inherit-doc/>";
        emitEditorBrowsableNeverAttribute();
        _out << nl << "public " << name << "(Ice20Decoder decoder)";
        _out.inc();
        _out << nl << ": base(decoder)";
        _out.dec();
        _out << sb;
        writeUnmarshalDataMembers(dataMembers, ns, Slice::ExceptionType);
        _out << eb;
    }

    string scoped = p->scoped();

    // Remote exceptions are always "preserved".

    _out << sp;
    _out << nl << "protected override void IceDecode(Ice11Decoder decoder)";
    _out << sb;
    _out << nl << "decoder.IceStartSlice();";
    writeUnmarshalDataMembers(dataMembers, ns, Slice::ExceptionType);
    _out << nl << "decoder.IceEndSlice();";
    if (base)
    {
        _out << nl << "base.IceDecode(decoder);";
    }
    _out << eb;

    _out << sp;
    _out << nl << "protected override void IceEncode(Ice11Encoder encoder)";
    _out << sb;
    _out << nl << "encoder.IceStartSlice(_iceTypeId);";
    writeMarshalDataMembers(dataMembers, ns, Slice::ExceptionType);
    if (base)
    {
        _out << nl << "encoder.IceEndSlice(lastSlice: false);";
        _out << nl << "base.IceEncode(encoder);";
    }
    else
    {
        _out << nl << "encoder.IceEndSlice(lastSlice: true);";
    }
    _out << eb;

    if (!base)
    {
        _out << sp;
        _out << nl << "protected override void IceEncode(Ice20Encoder encoder)";
        _out << sb;
        _out << nl << "encoder.EncodeString(_iceTypeId);";
        _out << nl << "encoder.EncodeString(Message);";
        _out << nl << "Origin.Encode(encoder);";
        writeMarshalDataMembers(dataMembers, ns, Slice::ExceptionType);
        _out << eb;
    }

    _out << eb;
}

bool
Slice::Gen::TypesVisitor::visitStructStart(const StructPtr& p)
{
    string name = fixId(p->name());
    string ns = getNamespace(p);
    _out << sp;

    writeTypeDocComment(p, getDeprecateReason(p));
    emitDeprecate(p, false, _out);
    emitCommonAttributes();
    emitCustomAttributes(p);
    _out << nl << "public ";
    if(p->hasMetadata("cs:readonly"))
    {
        _out << "readonly ";
    }

    _out << "partial struct " << name << " : global::System.IEquatable<" << name << ">";
    _out << sb;
    return true;
}

void
Slice::Gen::TypesVisitor::visitStructEnd(const StructPtr& p)
{
    string name = fixId(p->name());
    string scope = fixId(p->scope());
    string ns = getNamespace(p);
    MemberList dataMembers = p->dataMembers();

    emitEqualityOperators(name);

    _out << sp;
    _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/>.</summary>";
    for (const auto& member : dataMembers)
    {
        CommentInfo comment = processComment(member, "");
        writeDocCommentLines(_out, comment.summaryLines, "param", "name", paramName(member, "", false));
    }
    _out << nl << "public ";
    _out << name
         << spar
         << mapfn<MemberPtr>(dataMembers,
                             [&ns](const auto& i)
                             {
                                 return typeToString(i->type(), ns) + " " + fixId(i->name());
                             })
         << epar;
    _out << sb;
    for (const auto& i : dataMembers)
    {
        string paramName = fixId(i->name());
        string memberName = fixId(fieldName(i), Slice::ObjectType);
        _out << nl << (paramName == memberName ? "this." : "") << memberName  << " = " << paramName << ";";
    }
    _out << eb;

    _out << sp;
    _out << nl << "/// <summary>Constructs a new instance of <see cref=\"" << name << "\"/> from a decoder.</summary>";
    _out << nl << "public " << name << "(IceDecoder decoder)";
    _out << sb;
    writeUnmarshalDataMembers(dataMembers, ns, 0);
    _out << eb;

    _out << sp;
    _out << nl << "/// <inheritdoc/>";
    _out << nl << "public readonly override bool Equals(object? obj) => obj is " << name
         << " value && this.Equals(value);";

    if (!p->hasMetadata("cs:custom-equals"))
    {
        // Default implementation for Equals and GetHashCode
        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public readonly bool Equals(" << name << " other) =>";
        _out.inc();
        _out << nl;
        for (auto q = dataMembers.begin(); q != dataMembers.end();)
        {
            string mName = fixId(fieldName(*q), Slice::ObjectType);
            string lhs = "this." + mName;
            string rhs = "other." + mName;

            TypePtr mType = unwrapIfOptional((*q)->type());

            _out << lhs << " == " << rhs;

            if (++q != dataMembers.end())
            {
                _out << " &&" << nl;
            }
            else
            {
                _out << ";";
            }
        }
        _out.dec();

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public readonly override int GetHashCode()";
        _out << sb;
        _out << nl << "var hash = new global::System.HashCode();";
        for (const auto& dataMember : dataMembers)
        {
            string obj = "this." + fixId(fieldName(dataMember), Slice::ObjectType);
            TypePtr mType = unwrapIfOptional(dataMember->type());
            _out << nl << "hash.Add(" << obj << ");";
        }
        _out << nl << "return hash.ToHashCode();";
        _out << eb;
    }

    _out << sp;
    _out << nl << "/// <summary>Encodes the fields of this struct.</summary>";
    _out << nl << "public readonly void Encode(IceEncoder encoder)";
    _out << sb;
    writeMarshalDataMembers(dataMembers, ns, 0);
    _out << eb;

    _out << eb;
}

void
Slice::Gen::TypesVisitor::visitEnum(const EnumPtr& p)
{
    string name = fixId(p->name());
    string ns = getNamespace(p);
    string scoped = fixId(p->scoped());
    EnumeratorList enumerators = p->enumerators();

    // When the number of enumerators is smaller than the distance between the min and max values, the values are not
    // consecutive and we need to use a set to validate the value during unmarshaling.
    // Note that the values are not necessarily in order, e.g. we can use a simple range check for
    // enum E { A = 3, B = 2, C = 1 } during unmarshaling.
    const bool useSet = !p->isUnchecked() &&
        static_cast<int64_t>(enumerators.size()) < p->maxValue() - p->minValue() + 1;
    string underlying = p->underlying() ? typeToString(p->underlying(), "") : "int";

    _out << sp;
    emitDeprecate(p, false, _out);
    writeTypeDocComment(p, getDeprecateReason(p));
    emitCommonAttributes();
    emitCustomAttributes(p);
    _out << nl << "public enum " << name << " : " << underlying;
    _out << sb;
    bool firstEn = true;
    for (const auto& en : enumerators)
    {
        if (firstEn)
        {
            firstEn = false;
        }
        else
        {
            _out << ',';
            _out << sp;
        }

        writeTypeDocComment(en, getDeprecateReason(en));
        _out << nl << fixId(en->name());
        if (p->explicitValue())
        {
            _out << " = " << en->value();
        }
    }
    _out << eb;

    _out << sp;
    emitCommonAttributes();
    _out << nl << "/// <summary>Helper class for marshaling and unmarshaling <see cref=\"" << name << "\"/>.</summary>";
    _out << nl << "public static class " << p->name() << "Helper";
    _out << sb;
    if (useSet)
    {
        _out << sp;
        _out << nl << "public static readonly global::System.Collections.Generic.HashSet<" << underlying
            << "> EnumeratorValues =";
        _out.inc();
        _out << nl << "new global::System.Collections.Generic.HashSet<" << underlying << "> { ";
        firstEn = true;
        for (const auto& en : enumerators)
        {
            if (firstEn)
            {
                firstEn = false;
            }
            else
            {
                _out << ", ";
            }
            _out << en->value();
        }
        _out << " };";
        _out.dec();
    }

    _out << sp;
    _out << nl << "public static " << name << " As" << p->name() << "(this " << underlying << " value) =>";
    if (p->isUnchecked())
    {
        _out << " (" << name << ")value;";
    }
    else
    {
        _out.inc();
        if (useSet)
        {
            _out << nl << "EnumeratorValues.Contains(value)";
        }
        else
        {
            _out << nl << p->minValue() << " <= value && value <= " << p->maxValue();
        }
        _out << " ? (" << name
             << ")value : throw new IceRpc.InvalidDataException($\"invalid enumerator value '{value}' for "
             << fixId(p->scoped()) << "\");";
        _out.dec();
    }

    _out << sp;
    _out << nl << "public static " << name << " Decode" << p->name() << "(this IceDecoder decoder) =>";
    _out.inc();
    _out << nl << "As" << p->name() << "(decoder.";
    if (p->underlying())
    {
        _out << "Decode" << builtinSuffix(p->underlying()) << "()";
    }
    else
    {
        _out << "DecodeSize()";
    }
    _out << ");";
    _out.dec();

    _out << sp;
    _out << nl << "public static void Encode" << p->name() << "(this IceEncoder encoder, "
         << name << " value) =>";
    _out.inc();
    if (p->underlying())
    {
        _out << nl << "encoder.Encode" << builtinSuffix(p->underlying()) << "((" << underlying << ")value);";
    }
    else
    {
        _out << nl << "encoder.EncodeSize((int)value);";
    }
    _out.dec();
    _out << eb;
}

void
Slice::Gen::TypesVisitor::visitDataMember(const MemberPtr& p)
{
    ContainedPtr cont = ContainedPtr::dynamicCast(p->container());
    assert(cont);

    _out << sp;

    bool readonly = StructPtr::dynamicCast(cont) && cont->hasMetadata("cs:readonly");

    writeTypeDocComment(p, getDeprecateReason(p, true));
    emitDeprecate(p, true, _out);
    emitCustomAttributes(p);
    _out << nl << "public ";
    if(readonly)
    {
        _out << "readonly ";
    }
    _out << typeToString(p->type(), getNamespace(cont));
    _out << " " << fixId(fieldName(p), ExceptionPtr::dynamicCast(cont) ? Slice::ExceptionType : Slice::ObjectType);
    _out << ";";
}

Slice::Gen::ProxyVisitor::ProxyVisitor(IceUtilInternal::Output& out) :
    CsVisitor(out)
{
}

bool
Slice::Gen::ProxyVisitor::visitModuleStart(const ModulePtr& p)
{
    if(!p->hasInterfaceDefs())
    {
        return false;
    }
    openNamespace(p);
    return true;
}

void
Slice::Gen::ProxyVisitor::visitModuleEnd(const ModulePtr&)
{
    closeNamespace();
}

bool
Slice::Gen::ProxyVisitor::visitInterfaceDefStart(const InterfaceDefPtr& p)
{
    string name = p->name();
    const string ns = getNamespace(p);
    string prxInterface = interfaceName(p) + "Prx";
    string prxImpl = prxInterface.substr(1);

    _out << sp;
    writeProxyDocComment(p, getDeprecateReason(p));
    emitCommonAttributes();
    emitTypeIdAttribute(p->scoped());
    emitCustomAttributes(p);
    _out << nl << "public partial interface " << prxInterface;

    auto allBases = p->allBases();
    bool addServicePrx = none_of(allBases.begin(),
                                 allBases.end(),
                                 [](const auto& b) { return b->scoped() == "::IceRpc::Service"; }) &&
                         p->scoped() != "::IceRpc::Service";

    auto baseList = p->bases();
    if (!baseList.empty())
    {
        _out << " : ";
        vector<string> baseInterfaces =
            mapfn<InterfaceDefPtr>(baseList, [&ns](const auto& c)
                               {
                                   return getUnqualified(getNamespace(c) + "." +
                                                         interfaceName(c) + "Prx", ns);
                               });

        for (vector<string>::const_iterator q = baseInterfaces.begin(); q != baseInterfaces.end();)
        {
            _out << *q;
            if (++q != baseInterfaces.end())
            {
                _out << ", ";
            }
        }
    }
    _out << sb;

    auto operationList = p->operations();

    // Generate abstract methods and documentation
    for (const auto& operation : operationList)
    {
        string deprecateReason = getDeprecateReason(operation, true);
        string opName = fixId(operationName(operation));
        string asyncName = opName + "Async";

        _out << sp;
        writeOperationDocComment(operation, deprecateReason, false);
        if (!deprecateReason.empty())
        {
            _out << nl << "[global::System.Obsolete(\"" << deprecateReason << "\")]";
        }

        _out << nl << returnTaskStr(operation, ns, false) << " " << asyncName << spar
            << getInvocationParams(operation, ns, true) << epar << ";";
    }

    _out << eb;
    _out << sp;

    _out << nl << "/// <summary>Typed proxy struct. It implements <see cref=\"" << prxInterface << "\"/>"
        << " by sending requests to a remote IceRPC service.</summary>";
    emitCommonAttributes();
    emitTypeIdAttribute(p->scoped());
    emitCustomAttributes(p);
    _out << nl << "public readonly partial struct " << prxImpl << " : " << prxInterface;

    if (addServicePrx)
    {
        _out << ", IceRpc.IServicePrx";
    }

    _out << ", IPrx, global::System.IEquatable<" << prxImpl << ">";

    _out << sb;

    // Generate nested Request and Response classes if this interface has operations.
    bool generateRequestClass =
        find_if(operationList.begin(), operationList.end(), [](const auto& op) {
            return !op->params().empty() && (op->params().size() > 1 || !op->params().front()->stream());
            }) != operationList.end();

    bool generateResponseClass =
        find_if(operationList.begin(), operationList.end(), [](const auto& op) {
            return !op->returnType().empty() && (op->returnType().size() > 1 || !op->returnType().front()->stream()); })
            != operationList.end();

    if (generateRequestClass)
    {
        _out << nl << "/// <summary>Converts the arguments of each operation that takes arguments into a request "
            << "payload.</summary>";
        _out << nl << "public static class Request";
        _out << sb;
        for (const auto& operation : operationList)
        {
            auto params = operation->params();
            if (params.size() > 0 && (params.size() > 1 || !params.back()->stream()))
            {
                if (params.back()->stream())
                {
                    params.pop_back();
                }

                _out << sp;
                _out << nl << "/// <summary>Creates the request payload for operation " << operation->name() <<
                    ".</summary>";
                _out << nl << "/// <param name=\"prx\">Typed proxy to the target service.</param>";
                if (params.size() == 1)
                {
                    _out << nl << "/// <param name=\"arg\">The request argument.</param>";
                }
                else
                {
                    _out << nl << "/// <param name=\"args\">The request arguments.</param>";
                }
                _out << nl << "/// <returns>The payload.</returns>";

                _out << nl << "public static global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>> "
                    << fixId(operationName(operation)) << "(" << prxImpl << " prx, ";

                if (params.size() == 1)
                {
                    _out << toTupleType(params, ns, true) << " arg) =>";
                }
                else
                {
                    _out << "in " << toTupleType(params, ns, true) << " args) =>";
                }
                _out.inc();

                string ice11 = operation->sendsClasses(true) ? "Ice11" : "";

                if (params.size() == 1)
                {
                    _out << nl << "prx.Proxy.Create" << ice11 << "PayloadFromSingleArg(";
                }
                else
                {
                    _out << nl << "prx.Proxy.Create" << ice11 << "PayloadFromArgs(";
                }
                _out.inc();
                _out << nl << (params.size() == 1 ? "arg," : "in args,");
                _out << nl;
                writeOutgoingRequestEncodeAction(operation);
                string classFormat = opFormatTypeToString(operation);
                if (classFormat != "default")
                {
                    _out << "," << nl << classFormat;
                }
                _out << ");";
                _out.dec();
                _out.dec();
            }
        }
        _out << eb;
    }

    if (generateResponseClass)
    {
        _out << sp;
        _out << nl << "/// <summary>Holds a <see cref=\"ResponseDecodeFunc{T}\"/> for each non-void "
                << "remote operation defined in <see cref=\"" << interfaceName(p) << "Prx\"/>.</summary>";
        _out << nl << "public static class Response";
        _out << sb;

        for (const auto& operation : p->operations())
        {
            auto returns = operation->returnType();
            if (returns.size() > 0 && (returns.size() > 1 || !returns.back()->stream()))
            {
                _out << sp;
                string opName = fixId(operationName(operation));
                _out << nl << "/// <summary>The <see cref=\"ResponseDecodeFunc{T}\"/> for the return value "
                        << "type of operation " << operation->name() << ".</summary>";
                _out << nl << "public static " << toTupleType(returns, ns, false) << ' ' << opName;
                _out << "(IceRpc.IncomingResponse response, IceRpc.IInvoker? invoker, ";
                _out << "IceRpc.Slice.StreamParamReceiver? streamParamReceiver) =>";
                _out.inc();
                _out << nl << "response.ToReturnValue(";
                _out.inc();
                _out << nl << "invoker,";
                _out << nl << "_defaultIceDecoderFactories,";
                _out << nl;
                writeIncomingResponseDecodeFunc(operation);
                _out << ");";
                _out.dec();
                _out.dec();
            }
        }
        _out << eb;
    }

    // Static properties
    _out << sp;
    _out << nl << "/// <summary>The default path for services that implement Slice interface <c>" << name
        << "</c>.</summary>";
    _out << nl << "public static readonly string DefaultPath = typeof(" << prxImpl << ").GetDefaultPath();";

    _out << sp;
    _out << nl << "private static readonly DefaultIceDecoderFactories _defaultIceDecoderFactories ="
            << " new(typeof(" << prxImpl << ").Assembly);";

    // Non-static properties and fields
    _out << sp;
    _out << nl << "/// <summary>The proxy to the remote service.</summary>";
    _out << nl << "public IceRpc.Proxy Proxy { get; init; }";

    // Implicit upcast
    vector<string> allBaseImpls =
        mapfn<InterfaceDefPtr>(p->allBases(), [&ns](const auto& c)
                           {
                               return getUnqualified(getNamespace(c) + "." +
                                                     interfaceName(c).substr(1) + "Prx", ns);
                           });

    if (addServicePrx)
    {
        allBaseImpls.push_back(getUnqualified("IceRpc.ServicePrx", ns));
    }

    for (auto baseImpl : allBaseImpls)
    {
        _out << sp;
        _out << nl << "/// <summary>Implicit conversion to <see cref=\"" << baseImpl << "\"/>.</summary>";
        _out << nl << "public static implicit operator " << baseImpl << "(" << prxImpl
            << " prx) => new(prx.Proxy);";
    }

    // Equality operations
    emitEqualityOperators(prxImpl);

    // Static methods
    _out << sp;
    _out << nl << "/// <summary>Creates a new <see cref=\"" << prxImpl
        << "\"/> from the given connection and path.</summary>";
    _out << nl << "/// <param name=\"connection\">The connection. If it's an outgoing connection, the endpoint of the "
        << "new proxy is";
    _out << nl << "/// <see cref=\"Connection.RemoteEndpoint\"/>; otherwise, the new proxy has no endpoint.</param>";
    _out << nl << "/// <param name=\"path\">The path of the proxy. If null, the path is set to "
        << "<see cref=\"DefaultPath\"/>.</param>";
    _out << nl << "/// <param name=\"invoker\">The invoker. If null and connection is an incoming connection, the "
        << "invoker is set to";
    _out << nl << "/// the server's invoker.</param>";
    _out << nl << "/// <returns>The new proxy.</returns>";

    _out << nl << "public static " << prxImpl << " FromConnection(";
    _out.inc();
    _out << nl << "IceRpc.Connection connection,";
    _out << nl << "string? path = null,";
    _out << nl << "IceRpc.IInvoker? invoker = null) =>";
    _out << nl << "new(IceRpc.Proxy.FromConnection(connection, path ?? DefaultPath, invoker));";
    _out.dec();

    _out << sp;
    _out << nl << "/// <summary>Creates a new <see cref=\"" << prxImpl
        << "\"/> with the given path and protocol.</summary>";
    _out << nl << "/// <param name=\"path\">The path for the proxy.</param>";
    _out << nl << "/// <param name=\"protocol\">The proxy protocol.</param>";
    _out << nl << "/// <returns>The new proxy.</returns>";
    _out << nl << "public static " << prxImpl
        << " FromPath(string path, IceRpc.Protocol protocol = IceRpc.Protocol.Ice2) =>";
    _out.inc();
    _out << nl << "new(IceRpc.Proxy.FromPath(path, protocol));";
    _out.dec();

    _out << sp;
    _out << nl << "/// <summary>Creates a new <see cref=\"" << prxImpl
        << "\"/> from a string and invoker.</summary>";
    _out << nl << "/// <param name=\"s\">The string representation of the proxy.</param>";
    _out << nl << "/// <param name=\"invoker\">The invoker of the new proxy.</param>";
    _out << nl << "/// <returns>The new proxy</returns>";
    _out << nl << "/// <exception cref=\"global::System.FormatException\"><c>s</c> does not contain a valid string "
         << "representation of a proxy.</exception>";
    _out << nl << "public static " << prxImpl << " Parse(string s, IceRpc.IInvoker? invoker = null) => "
         << "new(IceRpc.Proxy.Parse(s, invoker));";

    _out << sp;
    _out << nl << "/// <summary>Creates a new <see cref=\"" << prxImpl
        << "\"/> from a string and invoker.</summary>";
    _out << nl << "/// <param name=\"s\">The proxy string representation.</param>";
    _out << nl << "/// <param name=\"invoker\">The invoker of the new proxy.</param>";
    _out << nl << "/// <param name=\"prx\">The new proxy.</param>";
    _out << nl << "/// <returns><c>true</c> if the s parameter was parsed successfully; otherwise, <c>false</c>."
         << "</returns>";
    _out << nl << "public static bool TryParse(string s, IceRpc.IInvoker? invoker, out "
        << prxImpl << " prx)";
    _out << sb;
    _out << nl << "if (IceRpc.Proxy.TryParse(s, invoker, out IceRpc.Proxy? proxy))";
    _out << sb;
    _out << nl << "prx = new(proxy);";
    _out << nl << "return true;";
    _out << eb;
    _out << nl << "else";
    _out << sb;
    _out << nl << "prx = default;";
    _out << nl << "return false;";
    _out << eb;
    _out << eb;
    _out << sp;
    _out << nl << "/// <summary>Constructs an instance of <see cref=\"" << prxImpl << "\"/>.</summary>";
    _out << nl << "/// <param name=\"proxy\">The proxy to the remote service.</param>";
    _out << nl << "public " << prxImpl << "(IceRpc.Proxy proxy) => Proxy = proxy;";

    // Equals + GetHashCode + ToString
    _out << sp;
    _out << nl << "/// <inheritdoc/>";
    _out << nl << "public bool Equals(" << prxImpl << " other) => Proxy.Equals(other.Proxy);";

    _out << sp;
    _out << nl << "/// <inheritdoc/>";
    _out << nl << "public override bool Equals(object? obj) => obj is " << prxImpl << " value && Equals(value);";

    _out << sp;
    _out << nl << "/// <inheritdoc/>";
    _out << nl << "public override int GetHashCode() => Proxy.GetHashCode();";

    _out << sp;
    _out << nl << "/// <inheritdoc/>";
    _out << nl << "public override string ToString() => Proxy.ToString();";

    // Base interface methods
    if (addServicePrx)
    {
        // TODO: do better with the new parser
        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public global::System.Threading.Tasks.Task<string[]> IceIdsAsync(";
        _out.inc();
        _out << nl << "IceRpc.Invocation? invocation = null,";
        _out << nl << "global::System.Threading.CancellationToken cancel = default) =>";
        _out << nl << "new IceRpc.ServicePrx(Proxy).IceIdsAsync(invocation, cancel);";
        _out.dec();

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public global::System.Threading.Tasks.Task<bool> IceIsAAsync(";
        _out.inc();
        _out << nl << "string id,";
        _out << nl << "IceRpc.Invocation? invocation = null,";
        _out << nl << "global::System.Threading.CancellationToken cancel = default) =>";
        _out << nl << "new IceRpc.ServicePrx(Proxy).IceIsAAsync(id, invocation, cancel);";
        _out.dec();

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public global::System.Threading.Tasks.Task IcePingAsync(";
        _out.inc();
        _out << nl << "IceRpc.Invocation? invocation = null,";
        _out << nl << "global::System.Threading.CancellationToken cancel = default) =>";
        _out << nl << "new IceRpc.ServicePrx(Proxy).IcePingAsync(invocation, cancel);";
        _out.dec();
    }

    for (const auto& operation : p->allBaseOperations())
    {
        string opName = fixId(operationName(operation));
        string asyncName = opName + "Async";

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public " << returnTaskStr(operation, ns, false) << " " << asyncName << spar
            << getInvocationParams(operation, ns, true) << epar << " =>";
        _out.inc();
        _out << nl << "new ";

        InterfaceDefPtr baseInterface = InterfaceDefPtr::dynamicCast(operation->container());
        string basePrxImpl = getUnqualified(getNamespace(baseInterface) + "." +
                                            interfaceName(baseInterface).substr(1) + "Prx", ns);

        _out << basePrxImpl << "(Proxy)." << asyncName << spar;

        for (const auto& param : operation->params())
        {
            _out << paramName(param);
        }
        _out << getEscapedParamName(operation, "invocation");
        _out << getEscapedParamName(operation, "cancel");
        _out << epar << ";";
        _out.dec();
    }

    return true;
}

void
Slice::Gen::ProxyVisitor::visitInterfaceDefEnd(const InterfaceDefPtr&)
{
    _out << eb; // prxImpl
}

void
Slice::Gen::ProxyVisitor::visitOperation(const OperationPtr& operation)
{
    auto returnType = operation->returnType();
    MemberPtr streamReturnParam;
    if (!returnType.empty() && returnType.back()->stream())
    {
        streamReturnParam = returnType.back();
        returnType.pop_back();
    }

    auto params = operation->params();
    MemberPtr streamParam;
    if (!params.empty() && params.back()->stream())
    {
        streamParam = params.back();
        params.pop_back();
    }

    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string deprecateReason = getDeprecateReason(operation, true);

    string ns = getNamespace(interface);
    string name = fixId(operationName(operation));
    string asyncName = name + "Async";
    bool oneway = operation->hasMetadata("oneway");

    TypePtr ret = operation->deprecatedReturnType();
    string retS = typeToString(operation->deprecatedReturnType(), ns);

    string invocation = getEscapedParamName(operation, "invocation");
    string cancel = getEscapedParamName(operation, "cancel");

    bool voidOp = returnType.empty();

    _out << sp;
    // TODO: it would be nice to output the parameters one per line, but this doesn't work with spar/epar.
    _out << nl << "public " << returnTaskStr(operation, ns, false) << " " << asyncName << spar
        << getInvocationParams(operation, ns, true) << epar << " =>";
    _out.inc();

    _out << nl << "Proxy.InvokeAsync(";
    _out.inc();
    _out << nl << "\"" << operation->name() << "\",";
    if (params.size() == 0)
    {
        _out << nl << "Proxy.CreateEmptyPayload(),";
    }
    else
    {
        // can't use 'in' for tuple as it's an expression
        _out << nl << "Request." << name << "(this, " << toTuple(params) << "),";
    }
    if (voidOp && !streamReturnParam)
    {
        _out << nl << "_defaultIceDecoderFactories,";
    }
    if (streamParam)
    {
        TypePtr streamT = streamParam->type();
        BuiltinPtr builtin = BuiltinPtr::dynamicCast(streamT);

        if (builtin && builtin->kind() == Builtin::KindByte)
        {
            _out << nl << "new IceRpc.Slice.ByteStreamParamSender(" << paramName(streamParam) << "),";
        }
        else
        {
            _out << nl << "new IceRpc.Slice.AsyncEnumerableStreamParamSender";
            _out << "<" << typeToString(streamT, ns) << ">(";
            _out.inc();
            _out << nl << paramName(streamParam) << ","
                 << nl << "Proxy.Encoding,"
                 << nl << encodeAction(streamT, ns, true, true) << "),";
            _out.dec();
        }
    }
    else
    {
        _out << nl << "streamParamSender: null,";
    }
    if (!voidOp)
    {
        _out << nl << "Response." << name << ",";
    }
    else if (streamReturnParam)
    {
        _out << nl << "(response, invoker, streamParamReceiver) =>";

        _out.inc();
        if (auto builtin = BuiltinPtr::dynamicCast(streamReturnParam->type());
            builtin && builtin->kind() == Builtin::KindByte)
        {
            _out << nl << "streamParamReceiver!.ToByteStream(),";
        }
        else
        {
            _out << nl << "streamParamReceiver!.ToAsyncEnumerable<" << typeToString(streamReturnParam->type(), ns) << ">(";
            _out.inc();
            _out << nl << "response,"
                 << nl << "invoker,"
                 << nl << "_defaultIceDecoderFactories,"
                 << nl << decodeFunc(streamReturnParam->type(), ns) << "),";
            _out.dec();
        }
        _out.dec();
    }

    _out << nl << invocation << ",";
    if (opCompressArgs(operation))
    {
        _out << nl << "compress: true,";
    }
    if (isIdempotent(operation))
    {
        _out << nl << "idempotent: true,";
    }
    if (voidOp && oneway)
    {
        _out << nl << "oneway: true,";
    }
    if (streamReturnParam)
    {
        _out << nl << "returnStreamParamReceiver: true,";
    }
    _out << nl << "cancel: " << cancel << ");";
    _out.dec();
    _out.dec();

    // TODO: move this check to the Slice parser.
    if (oneway && !voidOp)
    {
        const UnitPtr ut = operation->unit();
        const DefinitionContextPtr dc = ut->findDefinitionContext(operation->file());
        assert(dc);
        dc->error(operation->file(), operation->line(), "only void operations can be marked oneway");
    }
}

void
Slice::Gen::ProxyVisitor::writeOutgoingRequestEncodeAction(const OperationPtr& operation)
{
    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string ns = getNamespace(interface);

    auto params = operation->params();
    if (!params.empty() && params.back()->stream())
    {
        params.pop_back();
    }

    // When the operation's parameter is a T? where T is an interface or a class, there is a built-in encoder, so
    // defaultEncodeAction is true.
    bool defaultEncodeAction = params.size() == 1 && operation->paramsBitSequenceSize() == 0 && !params.front()->tagged();
    if (defaultEncodeAction)
    {
        _out << encodeAction(params.front()->type(), ns, true, true);
    }
    else
    {
        string encoderClass = operation->sendsClasses(true) ? "Ice11Encoder" : "IceEncoder";

        _out << "(" << encoderClass << " encoder, ";
        string inValue = params.size() > 1 ? "in " : "";
        _out << inValue << toTupleType(params, ns, true) << " value) =>";
        _out << sb;
        writeMarshal(operation, false);
        _out << eb;
    }
}

void
Slice::Gen::ProxyVisitor::writeIncomingResponseDecodeFunc(const OperationPtr& operation)
{
    InterfaceDefPtr interface = operation->interface();
    string ns = getNamespace(interface);

    auto returnType = operation->returnType();
    assert(!returnType.empty() && (returnType.size() > 1 || !returnType.back()->stream()));

    bool defaultDecodeFunc = returnType.size() == 1 && operation->returnBitSequenceSize() == 0 &&
        !returnType.front()->tagged();

    if (defaultDecodeFunc)
    {
        _out << decodeFunc(returnType.front()->type(), ns);
    }
    else if (returnType.size() > 0)
    {
        _out << "decoder =>";
        _out << sb;
        writeUnmarshal(operation, true);
        _out << eb;
    }
}

Slice::Gen::DispatcherVisitor::DispatcherVisitor(::IceUtilInternal::Output& out) :
    CsVisitor(out)
{
}

bool
Slice::Gen::DispatcherVisitor::visitModuleStart(const ModulePtr& p)
{
    if (!p->hasInterfaceDefs())
    {
        return false;
    }

    openNamespace(p);
    return true;
}

void
Slice::Gen::DispatcherVisitor::visitModuleEnd(const ModulePtr&)
{
    closeNamespace();
}

bool
Slice::Gen::DispatcherVisitor::visitInterfaceDefStart(const InterfaceDefPtr& p)
{
    InterfaceList bases = p->bases();
    string name = interfaceName(p);
    string ns = getNamespace(p);

    _out << sp;
    writeServantDocComment(p, getDeprecateReason(p));
    emitCommonAttributes();
    emitTypeIdAttribute(p->scoped());
    emitCustomAttributes(p);
    _out << nl << "public partial interface " << fixId(name);
    if (!bases.empty())
    {
        _out << " : ";
        for(InterfaceList::const_iterator q = bases.begin(); q != bases.end();)
        {
            _out << getUnqualified(getNamespace(*q) + "." + interfaceName(*q), ns);
            if(++q != bases.end())
            {
                _out << ", ";
            }
        }
    }

    _out << sb;

    // Generate nested Request and Response classes if needed.
    auto operationList = p->operations();
    bool generateRequestClass =
        find_if(operationList.begin(), operationList.end(), [](const auto& op) {
            return !op->params().empty() && (op->params().size() > 1 || !op->params().front()->stream());
            }) != operationList.end();

    bool generateResponseClass =
        find_if(operationList.begin(), operationList.end(), [](const auto& op) {
            return !op->returnType().empty() && (op->returnType().size() > 1 || !op->returnType().front()->stream());
            }) != operationList.end();

    if (generateRequestClass)
    {
        _out << nl << "/// <summary>Provides static methods that read the arguments of requests.</summary>";
        _out << nl << "public static" << (bases.empty() ? "" : " new") << " class Request";
        _out << sb;

        for (auto operation : operationList)
        {
            auto params = operation->params();
            if (params.size() > 0 && (params.size() > 1 || !params.back()->stream()))
            {
                string propertyName = fixId(operationName(operation));
                _out << sp;
                _out << nl << "/// <summary>Decodes the argument" << (params.size() > 1 ? "s " : " ")
                     << "of operation " << propertyName << ".</summary>";

                _out << nl << "public static " << toTupleType(params, ns, false) << ' ' << fixId(operationName(operation));
                _out << "(IceRpc.IncomingRequest request) =>";
                _out.inc();
                _out << nl << "request.ToArgs(";
                _out.inc();
                _out << nl << "_defaultIceDecoderFactories,";
                _out << nl;
                writeIncomingRequestDecodeFunc(operation);
                _out << ");";
                _out.dec();
                _out.dec();
            }
        }
        _out << eb;
        _out << sp;
    }

    if (generateResponseClass)
    {
        _out << nl << "/// <summary>Provides static methods that create response payloads.</summary>";
        _out << nl << "public static" << (bases.empty() ? "" : " new") << " class Response";
        _out << sb;
        for (auto operation : operationList)
        {
            auto returns = operation->returnType();
            if (returns.size() > 0 && (returns.size() > 1 || !returns.back()->stream()))
            {
                if (returns.back()->stream())
                {
                    returns.pop_back();
                }

                _out << sp;
                _out << nl << "/// <summary>Creates a response payload for operation "
                     << fixId(operationName(operation)) << ".</summary>";
                _out << nl << "/// <param name=\"dispatch\">The dispatch properties.</param>";
                if (returns.size() == 1)
                {
                    _out << nl << "/// <param name=\"returnValue\">The return value to write into the new response payload.</param>";
                }
                else
                {
                    _out << nl << "/// <param name=\"returnValueTuple\">The return values to write into the new response payload.</param>";
                }
                _out << nl << "/// <returns>A new response payload.</returns>";
                _out << nl << "public static global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>> "
                     << fixId(operationName(operation))
                     << "(";
                _out.inc();
                _out << nl << "IceRpc.Dispatch dispatch,";

                if (returns.size() == 1)
                {
                    _out << nl << toTupleType(returns, ns, true) << " returnValue) =>";
                }
                else
                {
                    _out << nl << "in " << toTupleType(returns, ns, true) << " returnValueTuple) =>";
                }

                _out.inc();

                string ice11 = operation->returnsClasses(true) ? "Ice11" : "";

                if (returns.size() == 1)
                {
                    _out << nl << "dispatch.Encoding.Create" << ice11 << "PayloadFromSingleReturnValue(";
                }
                else
                {
                    _out << nl << "dispatch.Encoding.Create" << ice11 << "PayloadFromReturnValueTuple(";
                }

                _out.inc();
                _out << nl << (returns.size() == 1 ? "returnValue," : "in returnValueTuple,");
                _out << nl;
                writeOutgoingResponseEncodeAction(operation);
                string classFormat = opFormatTypeToString(operation);
                if (classFormat != "default")
                {
                    _out << "," << nl << classFormat;
                }
                _out << ");";
                _out.dec();
                _out.dec();
                _out.dec();
            }
        }
        _out << eb;
        _out << sp;
    }

    _out << nl << "private static readonly DefaultIceDecoderFactories _defaultIceDecoderFactories ="
        << " new(typeof(" << fixId(name) << ").Assembly);";

    for (const auto& op : p->operations())
    {
        writeReturnValueStruct(op);
        writeMethodDeclaration(op);
    }
    return true;
}

void
Slice::Gen::DispatcherVisitor::writeReturnValueStruct(const OperationPtr& operation)
{
    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string ns = getNamespace(interface);
    const string opName = pascalCase(operation->name());
    const string name = opName + "MarshaledReturnValue";
    auto returnType = operation->returnType();

    if (operation->hasMarshaledResult())
    {
        _out << sp;
        _out << nl << "/// <summary>Helper struct used to marshal the return value of " << opName << " operation."
             << "</summary>";
        _out << nl << "public struct " << name << " : global::System.IEquatable<" << name << ">";
        _out << sb;
        _out << nl << "/// <summary>The payload holding the marshaled response.</summary>";
        _out << nl << "public global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>> Payload { get; }";

        emitEqualityOperators(name);
        _out << sp;

        _out << nl << "/// <summary>Constructs a new <see cref=\"" << name  << "\"/> instance that";
        _out << nl << "/// immediately marshals the return value of operation " << opName << ".</summary>";
        _out << nl << "public " << name << spar
             << getNames(returnType, [ns](const auto& p)
                                     {
                                         return paramTypeStr(p, ns) + " " + paramName(p);
                                     })
             << ("IceRpc.Dispatch " + getEscapedParamName(operation, "dispatch"))
             << epar;
        _out << sb;
        _out << nl << "Payload = ";
        _out << getEscapedParamName(operation, "dispatch") << ".Encoding.";
        if (returnType.size() == 1)
        {
            _out << "CreatePayloadFromSingleReturnValue(";
        }
        else
        {
            _out << "CreatePayloadFromReturnValueTuple(";
        }
        _out.inc();
        _out << nl << toTuple(returnType) << ",";
        _out << nl;
        writeOutgoingResponseEncodeAction(operation);
        string classFormat = opFormatTypeToString(operation);
        if (classFormat != "default")
        {
            _out << "," << nl << "classFormat: " << opFormatTypeToString(operation);
        }
        _out << ");";
        _out.dec();
        _out << eb;

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public bool Equals(" << name << " other) => Payload.Equals(other.Payload);";

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public override bool Equals(object? obj) => obj is " << name << " value && Equals(value);";

        _out << sp;
        _out << nl << "/// <inheritdoc/>";
        _out << nl << "public override int GetHashCode() => Payload.GetHashCode();";

        _out << eb;
    }
}

void
Slice::Gen::DispatcherVisitor::writeMethodDeclaration(const OperationPtr& operation)
{
    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string ns = getNamespace(interface);
    string deprecateReason = getDeprecateReason(operation, true);
    const string name = fixId(operationName(operation) + "Async");

    _out << sp;
    writeOperationDocComment(operation, deprecateReason, true);
    _out << nl << "public ";

    _out << returnTaskStr(operation, ns, true);

    _out << " " << name << spar;
    _out << getNames(operation->params(),
                     [ns](const auto& param)
                     {
                        return paramTypeStr(param, ns, false) + " " + paramName(param);
                     });
    _out << ("IceRpc.Dispatch " + getEscapedParamName(operation, "dispatch"));
    _out << ("global::System.Threading.CancellationToken " + getEscapedParamName(operation, "cancel"));
    _out << epar << ';';
}

void
Slice::Gen::DispatcherVisitor::visitOperation(const OperationPtr& operation)
{
    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string ns = getNamespace(interface);
    string opName = operationName(operation);
    string name = fixId(opName + "Async");
    string internalName = "IceD" + opName + "Async";

    auto returnType = operation->returnType();
    MemberPtr streamReturnParam;
    if (!returnType.empty() && returnType.back()->stream())
    {
        streamReturnParam = returnType.back();
    }

    auto params = operation->params();
    MemberPtr streamParam;
    if (!params.empty() && params.back()->stream())
    {
        streamParam = params.back();
    }

    _out << sp;
    _out << nl << "[IceRpc.Slice.Operation(\"" << operation->name() << "\")]";
    _out << nl << "protected static ";
    _out << "async ";
    _out << "global::System.Threading.Tasks.ValueTask<(global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>>, IStreamParamSender?)>";
    _out << " " << internalName << "(";
    _out.inc();
    _out << nl << fixId(interfaceName(interface)) << " target,"
        << nl << "IceRpc.IncomingRequest request,"
        << nl << "IceRpc.Dispatch dispatch,"
        << nl << "global::System.Threading.CancellationToken cancel)";
    _out.dec();
    _out << sb;

    if (!streamParam)
    {
        _out << nl << "dispatch.StreamReadingComplete();";
    }
    if (!isIdempotent(operation))
    {
         _out << nl << "dispatch.CheckNonIdempotent();";
    }

    if (opCompressReturn(operation))
    {
        _out << nl << "dispatch.ResponseFeatures = IceRpc.Features.CompressPayloadExtensions.CompressPayload(dispatch.ResponseFeatures);";
    }

    // Even when the parameters are empty, we verify the payload is indeed empty (can contain tagged params
    // that we skip).
    if (params.empty())
    {
        _out << nl << "request.CheckEmptyArgs(_defaultIceDecoderFactories);";
    }

    if (params.size() == 1 && streamParam)
    {
        _out << nl << "var " << paramName(params.front(), "iceP_");
        if (auto builtin = BuiltinPtr::dynamicCast(streamParam->type());
            builtin && builtin->kind() == Builtin::KindByte)
        {
            _out << " = IceRpc.Slice.StreamParamReceiver.ToByteStream(request);";
        }
        else
        {
            _out << " = IceRpc.Slice.StreamParamReceiver.ToAsyncEnumerable<" << typeToString(streamParam->type(), ns) << ">(";
            _out.inc();
            _out << nl << "request,"
                << "_defaultIceDecoderFactories,"
                << nl << decodeFunc(streamParam->type(), ns) << ");";
            _out.dec();
        }
    }
    else if (params.size() >= 1)
    {
        _out << nl << "var " << (params.size() == 1 ? paramName(params.front(), "iceP_") : "args")
             << " = Request." << fixId(opName) << "(request);";
    }

    // The 'this.' is necessary only when the operation name matches one of our local variable (dispatch, decoder etc.)
    if (operation->hasMarshaledResult())
    {
        // TODO: support for stream param with marshaled result?

        _out << nl << "var returnValue = await target." << name << spar;
        if(params.size() > 1)
        {
            _out << getNames(params, [](const MemberPtr& param) { return "args." + fieldName(param); });
        }
        else if(params.size() == 1)
        {
            _out << paramName(params.front(), "iceP_");
        }
        _out << "dispatch"
             << "cancel" << epar << ".ConfigureAwait(false);";
        _out << nl << "return (returnValue.Payload, null);";
        _out << eb;
    }
    else
    {
        _out << nl;
        if (returnType.size() >= 1 || streamReturnParam)
        {
            _out << "var returnValue = ";
        }

        _out << "await target." << name << spar;
        if (params.size() > 1)
        {
            _out << getNames(params, [](const MemberPtr& param) { return "args." + fieldName(param); });
        }
        else if (params.size() == 1)
        {
            _out << paramName(params.front(), "iceP_");
        }
        _out << "dispatch" << "cancel" << epar << ".ConfigureAwait(false);";

        if (returnType.size() == 0 || (returnType.size() == 1 && streamReturnParam))
        {
            if (streamReturnParam)
            {
                _out << nl << "return (";
                _out.inc();
                _out << nl << "dispatch.Encoding.CreatePayloadFromVoidReturnValue(),";

                if (auto builtin = BuiltinPtr::dynamicCast(streamReturnParam->type());
                    builtin && builtin->kind() == Builtin::KindByte)
                {
                    _out << nl << "new IceRpc.Slice.ByteStreamParamSender(returnValue)";
                }
                else
                {
                    _out << nl << "new IceRpc.Slice.AsyncEnumerableStreamParamSender";
                    _out << "<" << typeToString(streamReturnParam->type(), ns) << ">(";
                    _out.inc();
                    _out << nl << "returnValue,"
                         << nl << "dispatch.Encoding,"
                         << nl << encodeAction(streamReturnParam->type(), ns, true, true) << ")";
                    _out.dec();
                }
                _out << ");";
                _out.dec();
            }
            else
            {
                _out << nl << "return (dispatch.Encoding.CreatePayloadFromVoidReturnValue(), null);";
            }
        }
        else if (streamReturnParam)
        {
            auto names = getNames(returnType, [](const MemberPtr &param) { return "returnValue." + fieldName(param); });
            auto streamName = names.back();
            names.pop_back();
            _out << nl << "return (";
            _out.inc();
            _out << nl << "Response." << fixId(opName) << "(dispatch, " << spar << names << epar << "),";

            if (auto builtin = BuiltinPtr::dynamicCast(streamReturnParam->type());
                builtin && builtin->kind() == Builtin::KindByte)
            {
                _out << nl << "new IceRpc.Slice.ByteStreamParamSender(" << streamName << ")";
            }
            else
            {
                _out << nl << "new IceRpc.Slice.AsyncEnumerableStreamParamSender";
                _out << "<" << typeToString(streamReturnParam->type(), ns) << ">(";
                _out.inc();
                _out << nl << streamName << ","
                     << nl << "dispatch.Encoding,"
                     << nl << encodeAction(streamReturnParam->type(), ns, true, true) << ")";
                _out.dec();
            }
            _out << ");";
            _out.dec();
        }
        else
        {
            _out << nl << "return (Response." << fixId(opName) << "(dispatch, returnValue), null);";
        }
        _out << eb;
    }
}

void
Slice::Gen::DispatcherVisitor::visitInterfaceDefEnd(const InterfaceDefPtr&)
{
    _out << eb; // interface
}

void
Slice::Gen::DispatcherVisitor::writeIncomingRequestDecodeFunc(const OperationPtr& operation)
{
    InterfaceDefPtr interface = operation->interface();
    string ns = getNamespace(interface);

    auto params = operation->params();
    assert(!params.empty() && (params.size() > 1 || !params.back()->stream()));

    bool defaultDecodeFunc = params.size() == 1 && operation->paramsBitSequenceSize() == 0 && !params.front()->tagged();

    if (defaultDecodeFunc)
    {
        _out << decodeFunc(params.front()->type(), ns);
    }
    else if (params.size() > 0)
    {
        _out << "decoder =>";
        _out << sb;
        writeUnmarshal(operation, false);
        _out << eb;
    }
}

void
Slice::Gen::DispatcherVisitor::writeOutgoingResponseEncodeAction(const OperationPtr& operation)
{
    InterfaceDefPtr interface = InterfaceDefPtr::dynamicCast(operation->container());
    string ns = getNamespace(interface);

    auto returns = operation->returnType();
    if (!returns.empty() && returns.back()->stream())
    {
        returns.pop_back();
    }

    // When the operation returns a T? where T is an interface or a class, there is a built-in encoder, so
    // defaultEncodeAction is true.
    bool defaultEncodeAction = returns.size() == 1 && operation->returnBitSequenceSize() == 0 && !returns.front()->tagged();
    if (defaultEncodeAction)
    {
        _out << encodeAction(returns.front()->type(), ns, true, true);
    }
    else
    {
        string encoderClass = operation->returnsClasses(true) ? "Ice11Encoder" : "IceEncoder";

        _out << "(" << encoderClass << " encoder, ";
        _out << (returns.size() > 1 ? "in " : "") << toTupleType(returns, ns, true) << " value";
        _out << ") =>";
        _out << sb;
        writeMarshal(operation, true);
        _out << eb;
    }
}
