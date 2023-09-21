using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BotTuner.Factories;
using ChessChallenge.Chess;

namespace BotTuner;

//Currently just a placeholder
public static class Program {
    public static async Task Main(string[] args) {
        using var runner = new MatchRunner();

        var littleBlue = new CSChessBotFactory("Bots/LittleBlue.cs");
        var frigBot = new CSChessBotFactory("Bots/FrigBot.cs");
        using var stro4k = new UCIBotFactory("Bots/stro", new Dictionary<string, string>() {
            {"hash", "224"},
            {"threads", "1"}
        });
        using var ice4 = new UCIBotFactory("Bots/ice4", new Dictionary<string, string>() {
            {"Hash", "224"},
            {"Threads", "1"}
        });

        ResStats stats = await runner.RunMatches<ResStats>(
            littleBlue, 
            new IChessBotFactory[] { frigBot, stro4k, ice4 }, 
            new[] { FenUtility.StartPositionFEN },
            60000, 0
        );
        Console.WriteLine($"Wins: {stats.NumWins}");
        Console.WriteLine($"Losses: {stats.NumLosses}");
        Console.WriteLine($"Draws: {stats.NumDraws}");
    }

    public struct ResStats : IAdditionOperators<ResStats, MatchRunner.MatchResult, ResStats> {
        public int NumWins, NumLosses, NumDraws;

        public static ResStats operator +(ResStats stats, MatchRunner.MatchResult res) {
            switch(res) {
                case MatchRunner.MatchResult.Win: stats.NumWins++; break;
                case MatchRunner.MatchResult.Loss: stats.NumLosses++; break;
                case MatchRunner.MatchResult.Draw: stats.NumDraws++; break;
            }
            return stats;
        }
    }
}
