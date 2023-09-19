using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ChessChallenge.API;

//TODO: add comments to this
namespace BotTuner.Factories {

    //Takes a path to a UCI compliant chess bot and makes a new IChessBot that runs it
    class UCIBotFactory : IChessBotFactory {
        private readonly string path;

        public UCIBotFactory(string path) {
            Console.WriteLine($"Loading {path}");
            this.path = path;
        }

        public IChessBot Create() => (IChessBot) new UCIBot(path);
    }

    //Takes a UCI compliant chess bot, runs it in a seperate process, and uses that for the bot
    class UCIBot : IChessBot {
        private readonly Process proc;

        public UCIBot(string bot) {
            proc = Process.Start(new ProcessStartInfo(bot) { RedirectStandardInput = true, RedirectStandardOutput = true })!;
            proc.StandardInput.WriteLine("hi");
            ReadUntil("uciok");
            proc.StandardInput.WriteLine("setoption name asm value false");
            proc.StandardInput.WriteLine("setoption name hash value 224");
            proc.StandardInput.WriteLine("ucinewgame");
        }

        ~UCIBot() => proc.Kill(true);

        public string ReadUntil(string cmd) {
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