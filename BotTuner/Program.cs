using System;
using System.Collections.Generic;
using BotTuner.Factories;
using ChessChallenge.API;

namespace BotTuner;

//Currently just a placeholder
internal static class Program {
    static void Main(string[] args) {
        var littleBlue = new CSChessBotFactory("Bots/LittleBlue.cs");
        var frigBot = new CSChessBotFactory("Bots/FrigBot.cs");
        var ice4 = new UCIBotFactory("Bots/ice4.exe", new Dictionary<string, string>() {
            {"Hash", "224"},
            {"Threads", "1"}
        });
        MatchRunner.RunMatches(
            littleBlue, 
            new IChessBotFactory[] { frigBot, ice4 }, 
            60000, 
            new[] { 
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1" 
            }
        );
    }
}
