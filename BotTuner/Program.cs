using System;
using System.Collections.Generic;
using BotTuner.Factories;
using ChessChallenge.API;

//Currently just a placeholder
namespace BotTuner
{
    class Program {
        static void Main(string[] args) {
            var runner = new MatchRunner(("Bots/LittleBlue.cs", null), new (string, Dictionary<string, string?>)[] { ("Bots/FrigBot.cs", null), ("Bots/TinyBot.cs", null) });
            runner.Test();
        }
    }
}
