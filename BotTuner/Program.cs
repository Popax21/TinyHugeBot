using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BotTuner.Factories;
using ChessChallenge.Chess;

namespace BotTuner;

//Currently just a placeholder
public static partial class Program {
    public const string BotName = "TinyHugeBot";

    private static MatchRunner? matchRunner;
    public static MatchRunner MatchRunner => matchRunner ??= new MatchRunner();

    public static async Task Main(string[] args) {
        try {
            //Command dispatch
            switch(args[0].ToLowerInvariant()) {
                case "compare": {
                    if(args.Length < 3) throw new Exception("Not enough arguments for compare command");

                    IChessBotFactory opponent;
                    if(Directory.Exists(args[2])) {
                        BotDBPath = args[2];
                        opponent = LoadLatestBotVersion();
                    } else opponent = LoadBot(args[2]);
        
                    await RunCompare(LoadBot(args[1]), opponent, args.Length <= 3 ? 21_500 : int.Parse(args[3]), args.Length <= 4 ? AllPositionCollections : args[4..]);
                } break;

                case "benchmark": {
                    if(args.Length < 3) throw new Exception("Not enough arguments for benchmark command");
                    BotDBPath = args[2];
                    await RunBenchmark(args[1], args.Length <= 3 ? AllPositionCollections : args[3..]);
                } break;

                default: throw new Exception($"Unknown command '{args[0]}'");
            }
        } finally {
            matchRunner?.Dispose();
        }
    }

    public static string[] AllPositionCollections = new string[] {
        "challenge", "nunn", "silversuite"
    };

    public static string[] LoadPositionCollection(string name) {
        using Stream stream = Assembly.GetCallingAssembly().GetManifestResourceStream(name) ?? throw new ArgumentException($"No EDB collection with name '{name}' found");
        using StreamReader reader = new StreamReader(stream);

        List<string> fens = new List<string>();
        while(reader.ReadLine() is string line) {
            line = line.Trim();
            if(line.Length <= 0) continue;
            if(line[0] == '[') continue;

            //Check if the line is a PGN or a FEN
            if(line.StartsWith("1.")) {
                //Read the entire PGN
                string pgn = line;
                while(reader.ReadLine() is string pgnLine && pgnLine.Length > 0) pgn += " " + pgnLine.Trim();

                //Parse the PGN
                Move[] moves = PGNLoader.MovesFromPGN(pgn);

                //Replay the moves, and save the FEN
                Board board = new Board();
                board.LoadStartPosition();
                foreach(Move move in moves) board.MakeMove(move, false);
                fens.Add(FenUtility.CurrentFen(board));
            } else fens.Add(line);
        }

        Console.WriteLine($"Loaded {fens.Count} FENs from position collection '{name}'");
        return fens.ToArray();
    }
}
