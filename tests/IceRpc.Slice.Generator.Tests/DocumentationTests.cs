// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IceRpc.Slice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DocumentationTests
{
    private const string MemberPrefix = "M:IceRpc.Slice.Generator.Tests.";

    private const string TrailingParams = "IceRpc.Features.IFeatureCollection,System.Threading.CancellationToken)";

    [TestCase("IMySessionManager")]
    [TestCase("IMySessionManagerService")]
    public void Single_return_gets_returns_doc_comment(string interfaceName)
    {
        // Arrange / Act
        XElement member = GetMember(
            $"{MemberPrefix}{interfaceName}.CreateSessionAsync(IceRpc.Slice.Generator.Tests.MyAuthToken,{TrailingParams}");

        // Assert
        Assert.That(member.Element("returns")?.Value, Is.EqualTo("The new session."));
    }

    [TestCase("IMySessionManager")]
    [TestCase("IMySessionManagerService")]
    public void Tuple_return_gets_returns_doc_comment_with_one_item_per_field(string interfaceName)
    {
        // Arrange / Act
        XElement member = GetMember($"{MemberPrefix}{interfaceName}.GetStatusAsync(System.String,{TrailingParams}");
        var items = member.Element("returns")!.Element("list")!.Elements("item")
            .ToDictionary(item => item.Element("term")!.Value, item => item.Element("description")!.Value);

        // Assert
        Assert.That(member.Element("returns")!.Value.Trim(), Does.StartWith("A tuple containing:"));
        Assert.That(items["Status"], Is.EqualTo("The status."));
        Assert.That(items["LastSeen"], Is.EqualTo("The time at what the user was last seen."));
    }

    private static XElement GetMember(string name) =>
        XDocument.Load(Path.ChangeExtension(typeof(DocumentationTests).Assembly.Location, ".xml"))
            .Descendants("member")
            .Single(m => m.Attribute("name")!.Value == name);
}
