// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Reflection;
using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReceiptsReq2234LogicTests
{
    [Fact]
    public void StringOf100Bytes_UsesStr8Header()
    {
        byte[] bytes = MessagePackSerializer.Serialize(new string('a', 100));
        Assert.Equal(0xD9, bytes[0]);
        Assert.Equal(102, bytes.Length);
    }

    [Fact]
    public void WriteStringHeader100_UsesStr8Header()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteStringHeader(100);
        writer.Flush();
        byte[] bytes = buffer.WrittenSpan.ToArray();
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0xD9, bytes[0]);
        Assert.Equal(100, bytes[1]);
    }

    [Fact]
    public async Task SuppressSourceGeneration_StillReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int Value { get; set; }
            }
            """;

        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assembly generatorAssembly = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        var analyzers = generatorAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0 && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }
}
