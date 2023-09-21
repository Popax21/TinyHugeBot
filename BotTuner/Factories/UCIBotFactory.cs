using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using API=ChessChallenge.API;

namespace BotTuner.Factories; 

//Takes a path to a UCI compliant chess bot and makes a new IChessBot that runs it
public class UCIBotFactory : IChessBotFactory, IDisposable {
    private static string ReadUntil(Process proc, string cmd) {
        //Read stdout until a specific message is seen
        while (proc.StandardOutput.ReadLine() is string msg)
            if (msg.StartsWith(cmd))
                return msg;

        throw new Exception();
    }

    private readonly string execPath;
    private readonly Dictionary<string, string> uciOptions;

    private readonly List<Process> processes = new List<Process>();
    private readonly ThreadLocal<Process> threadProc = new ThreadLocal<Process>();

    private Process ThreadProcess {
        get {
            if (threadProc.Value?.HasExited ?? true) {
                //Start an instance of the chosen bot
                Process proc = Process.Start(new ProcessStartInfo(execPath) { RedirectStandardInput = true, RedirectStandardOutput = true })!;
                proc.StandardInput.WriteLine("hi");
                ReadUntil(proc, "uciok");

                //Set options for the bot
                foreach (var opt in uciOptions)
                    proc.StandardInput.WriteLine($"setoption name {opt.Key} value {opt.Value}");

                processes.Add(proc);

                threadProc.Value = proc;
            }

            return threadProc.Value;
        }
    }

    public UCIBotFactory(string path, Dictionary<string, string> options) {
        execPath = path;
        uciOptions = options;

        //Store name for display purposes
        Name = Path.GetFileNameWithoutExtension(path);
    }

    public void Dispose() {
        processes.ForEach(p => p.Kill(true));
        processes.Clear();
    }
    ~UCIBotFactory() => Dispose();

    public string Name { get; }
    public API.IChessBot CreateBot() => new UCIBot(ThreadProcess);

    private sealed class UCIBot : API.IChessBot {
        private readonly Process proc;

        public UCIBot(Process proc) {
            //Store the bot process
            this.proc = proc;

            //Start a new game
            proc.StandardInput.WriteLine("ucinewgame");
        }

        public API.Move Think(API.Board board, API.Timer timer) {
            //Set board position
            proc.StandardInput.WriteLine($"position fen {board.GetFenString()}");

            //Start searching for best move given the time remaining
            int wtime, btime;
            (wtime, btime) = board.IsWhiteToMove ? (timer.MillisecondsRemaining, timer.OpponentMillisecondsRemaining) : (timer.OpponentMillisecondsRemaining, timer.MillisecondsRemaining);
            proc.StandardInput.WriteLine($"go wtime {wtime} btime {btime} winc {timer.IncrementMilliseconds} binc {timer.IncrementMilliseconds}");

            //Get the best move from the output
            string bestMove = ReadUntil(proc, "bestmove")[8..].Trim();
            return new API.Move(bestMove, board);
        }
    }
}