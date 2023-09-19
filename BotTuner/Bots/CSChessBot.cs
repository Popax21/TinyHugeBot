using System;
using System.IO;
using System.Linq;
using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BotTuner.Bots {

    //Takes a MyBot.cs file, compiles it at runtime, and uses that for the bot
    //Uses a lot of code from https://laurentkempe.com/2019/02/18/dynamically-compile-and-run-code-using-dotNET-Core-3.0/
    class CSChessBot : IChessBot {

        //Stores the chess bot contained in the source file
        private readonly IChessBot bot;

        public CSChessBot(string path) {
            Console.WriteLine($"Starting {path}...");

            //Read and parse the input file
            var sourceCode = File.ReadAllText(path);
            var codeString = SourceText.From(sourceCode);
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(codeString, options);

            //Create necessary assembly references
            var references = new MetadataReference[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Board).Assembly.Location),
            };

            //Compile the file
            var compiled = CSharpCompilation.Create("CSCB",
                new[] { parsedSyntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
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
                    throw new Exception("");
                }

                peStream.Position = 0;
                emitted = peStream.ToArray();
            }

            //Load the assembly and create the chess bot instance
            //See BotBuilder.Launchpad for a better explanation of this
            //There is probably a better way of doing this here but it should work anyway
            System.ResolveEventHandler asmResolveCB = (_, _) => AppDomain.CurrentDomain.Load(emitted);
            AppDomain.CurrentDomain.AssemblyResolve += asmResolveCB;
            bot = (IChessBot) AppDomain.CurrentDomain.CreateInstanceAndUnwrap("CSCB", "MyBot");
            AppDomain.CurrentDomain.AssemblyResolve -= asmResolveCB;
        }

        public Move Think(Board board, Timer timer) => bot.Think(board, timer);
    }
}
