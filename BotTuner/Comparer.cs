using System;
using System.Globalization;
using System.Threading.Tasks;
using BotTuner.Factories;

namespace BotTuner;

public static partial class Program {
    public static async Task RunCompare(string botAPath, string botBPath, int timerMs, string[] posCollections) {
        //Load bots
        IChessBotFactory botA = LoadBot(botAPath), botB = LoadBot(botBPath);

        double[] scores = new double[posCollections.Length];
        for(int i = 0; i < posCollections.Length; i++) {
            Console.WriteLine($"Comparing against position collection '{posCollections[i]}'...");

            //Load starting FENs
            string[] startFens = LoadPositionCollection(posCollections[i]);

            //Run matches
            await MatchRunner.RunMatches(botA, new[] { botB }, startFens, BenchmarkTimerMs, 0, (match, opponent, res) => {
                scores[i] += res switch {
                    MatchRunner.MatchResult.Win => 1.0,
                    MatchRunner.MatchResult.Draw => 0.5,
                    MatchRunner.MatchResult.Loss => 0.0,
                    _ => throw new Exception("Invalid match result")
                };
            });

            //Normalize the score
            scores[i] /= 2*startFens.Length;
        }

        //Print results
        Console.WriteLine();
        Console.WriteLine(">>>>>>>>>> RESULTS <<<<<<<<<<");
        Console.WriteLine();
        for(int i = 0; i < posCollections.Length; i++) {
            Console.WriteLine($"Position Collection '{posCollections[i]}': {scores[i].ToString("F5", CultureInfo.InvariantCulture)}");
        }
    }
}