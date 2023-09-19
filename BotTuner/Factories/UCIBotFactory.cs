using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ChessChallenge.API;

namespace BotTuner.Factories {

    //Takes a path to a UCI compliant chess bot and makes a new IChessBot that runs it
    class UCIBotFactory : IChessBotFactory {
        private readonly Process proc;
        public readonly string name;

        public UCIBotFactory(string path, Dictionary<string, string> options) {
            Console.WriteLine($"Loading {path}...");

            //Start an instance of the chosen bot
            proc = Process.Start(new ProcessStartInfo(path) { RedirectStandardInput = true, RedirectStandardOutput = true })!;
            proc.StandardInput.WriteLine("hi");
            ReadUntil("uciok");

            //Set options for the bot
            foreach (var opt in options) {
                proc.StandardInput.WriteLine($"setoption name {opt.Key} value {opt.Value}");
            }

            //Store name for display purposes
            name = Path.GetFileNameWithoutExtension(path);

            Console.WriteLine($"Finished loading {path}!");
        }

        public string ReadUntil(string cmd) {
            //Read stdout until a specific message is seen
            while (proc.StandardOutput.ReadLine() is string msg) {
                if (msg.StartsWith(cmd)) return msg;
            }
            throw new Exception();
        }

        public IChessBot Create() => new UCIBot(proc);

        public string GetName() => name;
    }

    //Takes a process of a UCI compliant chess bot and uses that for the bot
    class UCIBot : IChessBot {
        private readonly Process proc;

        public UCIBot(Process proc) {
            //Start a new game
            proc.StandardInput.WriteLine("ucinewgame");

            //Store the bot process
            this.proc = proc;
        }

        public string ReadUntil(string cmd) {
            //Read stdout until a specific message is seen
            while (proc.StandardOutput.ReadLine() is string msg)
            {
                if (msg.StartsWith(cmd)) return msg;
            }
            throw new Exception();
        }

        public Move Think(Board board, Timer timer) {
            //Set board position
            proc.StandardInput.WriteLine($"position fen {board.GetFenString()}");

            //Start searching for best move given the time remaining
            int wtime, btime;
            (wtime, btime) = board.IsWhiteToMove ? (timer.MillisecondsRemaining, timer.OpponentMillisecondsRemaining) : (timer.OpponentMillisecondsRemaining, timer.MillisecondsRemaining);
            proc.StandardInput.WriteLine($"go wtime {wtime} btime {btime} winc {timer.IncrementMilliseconds} binc {timer.IncrementMilliseconds}");

            //Get the best move from the output
            string bestMove = ReadUntil("bestmove")[8..].Trim();
            Console.WriteLine($"BEST MOVE: {bestMove}");
            return new Move(bestMove, board);
        }
    }
}