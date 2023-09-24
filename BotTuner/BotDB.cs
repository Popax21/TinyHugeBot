using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BotTuner.Factories;

namespace BotTuner;

public static partial class Program {
    private static Dictionary<string, string> UCIBotOptions = new Dictionary<string, string>() {
        {"Hash", "224"},
        {"Threads", "1"}
    };

    public static string BotDBPath = null!;

    public static IChessBotFactory LoadBot(string path) {
        if(Path.GetExtension(path) == ".cs") return new CSChessBotFactory(path);
        else return new UCIBotFactory(path, UCIBotOptions);
    }

    public static IChessBotFactory[] LoadAllBots(string dir) {
        Console.WriteLine($"Loading all bots from '{dir}'...");
        return Directory.GetFiles(dir).Order().Select(LoadBot).ToArray();
    }

    public static IChessBotFactory LoadLatestBotVersion()
        => LoadBot(Directory.GetFiles("PrevVers").Order().Last());

    public static IChessBotFactory[] LoadPrevBotVers()
        => LoadAllBots(Path.Combine(BotDBPath, "PrevVers"));

    public static IChessBotFactory[] LoadExternalRefBots()
        => LoadAllBots(Path.Combine(BotDBPath, "ExtRef"));

    public static void AddBotVersionToDB(string newBotPath) {
        //Determine the next version number for this bot
        int botVer = 1;
        string verPath;
        while(File.Exists(verPath = Path.Combine(BotDBPath, "PrevVers", $"{BotName}.v{botVer:d2}.cs"))) botVer++;

        //Save the bot
        File.Copy(newBotPath, verPath);
        Console.WriteLine($"Saved '{newBotPath}' as {BotName} v{botVer:d2} [{verPath}]");
    }
}