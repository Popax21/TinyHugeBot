using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BotTuner.Factories; 

//Takes a MyBot.cs file, compiles it at runtime, and cretaes a new IChessBot from it
//Uses a lot of code from https://laurentkempe.com/2019/02/18/dynamically-compile-and-run-code-using-dotNET-Core-3.0/
public class CSChessBotFactory : IChessBotFactory {
    private readonly Assembly assembly;

    public CSChessBotFactory(string path) {
        Console.WriteLine($"Loading CS bot '{path}'...");

        //Store name for display purposes
        Name = Path.GetFileNameWithoutExtension(path);

        //Read and parse the input file
        var botSrc = SourceText.From(File.ReadAllText(path));
        var parseOpts = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(botSrc, parseOpts);

        //Create necessary assembly references
        var asmLocation = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[] {
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "mscorlib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.Core.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(asmLocation, "System.Numerics.dll")),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Board).Assembly.Location),
        };

        //Compile the file
        var compiledBot = CSharpCompilation.Create($"CSBot_{Name}",
            new[] { parsedSyntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default
            )
        );

        //Emit the assembly
        using var peStream = new MemoryStream();
        var emittedAsm = compiledBot.Emit(peStream);
        if (!emittedAsm.Success) {
            Console.WriteLine($"Compilation of CS bot '{path}' failed!");

            //List failures
            foreach (var diagnostic in emittedAsm.Diagnostics)
                if (diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error)
                    Console.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());

            //Halt program
            throw new Exception($"Compilation of CS bot '{path}' failed");
        }

        //Load the assembly
        peStream.Position = 0;
        assembly = AssemblyLoadContext.Default.LoadFromStream(peStream);

        Console.WriteLine($"Finished loading CS bot '{path}'!");
    }

    public string Name { get; }
    public IChessBot CreateBot() => (IChessBot) assembly.CreateInstance("MyBot")!;
}