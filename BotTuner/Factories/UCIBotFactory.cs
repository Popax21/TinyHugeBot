using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ChessChallenge.API;

namespace BotTuner.Factories {

    //Takes a path to a UCI compliant chess bot and makes a new IChessBot that runs it
    class UCIBotFactory : IChessBotFactory {
        private readonly string path;
        private readonly Process proc;

        public UCIBotFactory(string path) {
            Console.WriteLine($"Loading {path}...");

            //Start an instance of the chosen bot
            proc = Process.Start(new ProcessStartInfo(path) { RedirectStandardInput = true, RedirectStandardOutput = true })!;
            proc.StandardInput.WriteLine("hi");
            ReadUntil("uciok");

            //Set options depending on if STRO is used or if ice4 is used
            if (path == "stro" || path == "stro.exe") {
                proc.StandardInput.WriteLine("setoption name asm value false");
                proc.StandardInput.WriteLine("setoption name hash value 224");
            } else {

            }

            //Store the path for passing to the UCIBot
            this.path = path;
        }

        public string ReadUntil(string cmd) {
            //Read stdout until a specific message is seen
            while (proc.StandardOutput.ReadLine() is string msg) {
                if (msg.StartsWith(cmd)) return msg;
            }
            throw new Exception();
        }


        public IChessBot Create() => new UCIBot(path, proc);
    }

    //Takes a process of a UCI compliant chess bot and uses that for the bot
    class UCIBot : IChessBot {
        private readonly Process proc;

        private readonly string bot;

        public UCIBot(string bot, Process proc) {
            //Start a new game
            proc.StandardInput.WriteLine("ucinewgame");

            //Store the bot process and it's name for future use
            this.bot = bot;
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
            proc.StandardInput.WriteLine($"position fen {board.GetFenString()}");

            int wtime, btime;
            (wtime, btime) = board.IsWhiteToMove ? (timer.MillisecondsRemaining, timer.OpponentMillisecondsRemaining) : (timer.OpponentMillisecondsRemaining, timer.MillisecondsRemaining);
            proc.StandardInput.WriteLine($"go wtime {wtime} winc {timer.IncrementMilliseconds} btime {btime} binc {timer.IncrementMilliseconds}");

            string bestMove = ReadUntil("bestmove")[8..].Trim();
            Console.WriteLine($"BEST MOVE: {bestMove}");
            return new Move(bestMove, board);
        }
    }
}