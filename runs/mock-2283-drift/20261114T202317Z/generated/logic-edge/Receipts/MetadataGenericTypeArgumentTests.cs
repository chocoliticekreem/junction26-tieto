// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericTypeArgumentTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UnattributedTypeArgumentOfReferencedGenericClass_StillReportsMsgPack003()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        string libSource = """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public class Item<T>
                {
                    [Key(0)]
                    public T Value { get; set; }
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        string appSource = """
            using MessagePack;
            using Lib;
            namespace App
            {
                [MessagePackObject]
                public class Cmd
                {
                    [Key(0)]
                    public Item<Bad> Member { get; set; }
                }

                public class Bad
                {
                    public int X { get; set; }
                }
            }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource) },
            references.Append(libRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Contains(diagnostics, d => d.Id == "MsgPack003");
    }
}
