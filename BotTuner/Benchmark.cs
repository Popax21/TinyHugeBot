using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BotTuner.Factories;

namespace BotTuner;

public static partial class Program {
    public const int BenchmarkTimerMs = 7_500;
    public const int NumBenchmarkLastVersions = 3;

    public static async Task RunBenchmark(string targetBotPath, string[] posCollections) {
        //Load bots
        IChessBotFactory targetBot = new CSChessBotFactory(targetBotPath);
        IChessBotFactory[] prevBotVers = LoadPrevBotVers();
        IChessBotFactory[] opponents = Enumerable.Range(0, prevBotVers.Length).Where(idx => idx >= prevBotVers.Length - NumBenchmarkLastVersions).Select(idx => prevBotVers[idx]).ToArray();

        double[,] scores = new double[opponents.Length, posCollections.Length];
        for(int i = 0; i < posCollections.Length; i++) {
            Console.WriteLine($"Benchmarking position collection '{posCollections[i]}'...");

            //Load starting FENs
            string[] startFens = LoadPositionCollection(posCollections[i]);

            //Run matches
            await MatchRunner.RunMatches(targetBot, opponents, startFens, BenchmarkTimerMs, 0, (match, opponent, res) => {
                scores[Array.IndexOf(opponents, opponent), i] += res switch {
                    MatchRunner.MatchResult.Win => 1.0,
                    MatchRunner.MatchResult.Draw => 0.5,
                    MatchRunner.MatchResult.Loss => 0.0,
                    _ => throw new Exception("Invalid match result")
                };
            });

            //Normalize scores
            for(int j = 0; j < opponents.Length; j++) scores[j, i] /= 2*startFens.Length;
        }

        //Print results
        Console.WriteLine();
        Console.WriteLine(">>>>>>>>>> RESULTS <<<<<<<<<<");
        Console.WriteLine();
        for(int i = 0; i < posCollections.Length; i++) {
            Console.WriteLine($"Position Collection '{posCollections[i]}':");
            for(int j = 0; j < opponents.Length; j++) {
                Console.WriteLine($" - {opponents[j].Name}: {scores[j, i].ToString("F5", CultureInfo.InvariantCulture)}");
            }
            Console.WriteLine();
        }

        //Offer to add to the database
        Console.WriteLine("Add to the version database? (y/n)");
        if(Console.ReadLine()!.Trim() == "y") AddBotVersionToDB(targetBotPath);
    }
}