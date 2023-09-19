using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BotTuner.Factories {

    //Takes a MyBot.cs file, compiles it at runtime, and cretaes a new IChessBot from it
    //Uses a lot of code from https://laurentkempe.com/2019/02/18/dynamically-compile-and-run-code-using-dotNET-Core-3.0/
    class CSChessBotFactory : IChessBotFactory {
        private readonly byte[] assembly;

        public CSChessBotFactory(string path) {
            Console.WriteLine($"Loading {path}...");

            //Read and parse the input file
            var sourceCode = File.ReadAllText(path);
            var codeString = SourceText.From(sourceCode);
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(codeString, options);

            //Create necessary assembly references
            var asmLocation = Path.GetDirectoryName(typeof(object).Assembly.Location);
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
            var compiled = CSharpCompilation.Create("CSCB",
                new[] { parsedSyntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

            //Convert the compiled assembly to a byte[]
            byte[] emitted;
            using (var peStream = new MemoryStream()) {
                var result = compiled.Emit(peStream);

                //Make sure the compilation was successful
                if (!result.Success) {
                    Console.WriteLine($"Compilation of {path} failed!");

                    //List failures if unsuccessful
                    var failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
                    foreach (var diagnostic in failures) {
                        Console.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                    }

                    //Halt program
                    throw new Exception();
                }

                peStream.Position = 0;
                emitted = peStream.ToArray();
            }

            //Store the assembly for future use
            assembly = emitted;

            Console.WriteLine($"Finished loading {path}!");
        }

        public IChessBot Create() => (IChessBot) Assembly.Load(assembly).CreateInstance("MyBot");
    }
}
