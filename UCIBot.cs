using System;
using System.Diagnostics;
using ChessChallenge.API;

class MyBot : IChessBot {
    private readonly Process proc;

    public MyBot() {
        proc = Process.Start(new ProcessStartInfo("stro") { RedirectStandardInput = true, RedirectStandardOutput = true })!;
        proc.StandardInput.WriteLine("hi");
        ReadUntil("uciok");
        proc.StandardInput.WriteLine("setoption name asm value false");
        proc.StandardInput.WriteLine("setoption name hash value 224");
        proc.StandardInput.WriteLine("ucinewgame");
    }

    ~MyBot() => proc.Kill(true);

    public string ReadUntil(string cmd) {
        while(proc.StandardOutput.ReadLine() is string msg) {
            if(msg.StartsWith(cmd)) return msg;
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