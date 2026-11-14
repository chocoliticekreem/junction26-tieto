// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Receipts.GeneratedTests;

public class DerivedKeySuppressionReceiptTests
{
    [Fact]
    public async Task DerivedKeyDoesNotSuppressMemberTypeDiagnostics()
    {
        const string source = """
            using MessagePack;

            public sealed class CustomKeyAttribute : KeyAttribute
            {
                public CustomKeyAttribute()
                    : base(0)
                {
                }
            }

            [MessagePackObject]
            public partial class Container
            {
                [CustomKey]
                public Unannotated Value { get; set; }
            }

            public class Unannotated
            {
                public int Value { get; set; }
            }
            """;

        CSharpParseOptions parseOptions = new();
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDerivedKeySuppression",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            optionsProvider: new SuppressedSourceGenerationOptionsProvider());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MsgPack003");
    }

    private sealed class SuppressedSourceGenerationOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions EmptyOptions = new DictionaryAnalyzerConfigOptions(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.SuppressSourceGeneration"] = "true",
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions;
    }

    private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> values;

        public DictionaryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        {
            this.values = values;
        }

        public override bool TryGetValue(string key, out string? value) => this.values.TryGetValue(key, out value);
    }
}
