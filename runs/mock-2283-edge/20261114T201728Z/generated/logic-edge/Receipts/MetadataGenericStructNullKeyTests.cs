// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructNullKeyTests
{
    private const string LibSource = @"
using MessagePack;
namespace Lib
{
    [MessagePackObject]
    public struct Item<T>
    {
        [Key(null)]
        public string Name { get; set; }
    }
}
";

    private const string AppSource = @"
using MessagePack;
using Lib;
namespace App
{
    [MessagePackObject]
    public class Cmd
    {
        [Key(0)]
        public Item<int> Member { get; set; }
    }
}
";

    private readonly ITestOutputHelper logger;

    public MetadataGenericStructNullKeyTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync()
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        var lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        var appRefs = new List<MetadataReference>(refs) { libRef };
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource) },
            appRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public async Task Generator_DoesNotThrow_OnMetadataGenericStructWithNullKey()
    {
        var compilation = await CreateAppCompilationAsync();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        foreach (var r in result.Results)
        {
            if (r.Exception is not null)
            {
                this.logger.WriteLine(r.Exception.ToString());
            }
        }

        Assert.All(result.Results, r => Assert.Null(r.Exception));
    }

    [Fact]
    public async Task Analyzer_DoesNotThrow_OnMetadataGenericStructWithNullKey()
    {
        var compilation = await CreateAppCompilationAsync();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (var d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
