using ChessChallenge.API;
using System;
using HugeBot;

namespace HugeUCI {
    class Program {

        static MyBot bot;
        static ChessChallenge.Chess.Board board;

        static void Main(string[] args) {
            while (true) {
                string s = Console.ReadLine();
                string[] command = s.Split(' ');

                switch (command[0]) {
                    case "uci" or "hi":
                        Console.WriteLine("id name TinyHugeBot");
                        Console.WriteLine("id author Popax21 & atpx8");
                        Console.WriteLine("uciok");
                        break;

                    case "isready":
                        Console.WriteLine("readyok");
                        break;

                    case "ucinewgame":
                        bot = new MyBot();
                        break;

                    case "position":
                        board = new ChessChallenge.Chess.Board();
                        int offset;
                        if (command[1] == "startpos") {
                            board.LoadPosition("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                            offset = 3;
                        } else {
                            board.LoadPosition(string.Join(" ", command[2..8]));
                            offset = 9;
                        }
                        for (; offset < command.Length; offset++) {
                            Board board2 = new Board(board);
                            var move = new Move(command[offset], board2);
                            board.MakeMove(new ChessChallenge.Chess.Move(move.RawValue), false);
                        }
                        break;

                    case "go":
                        var timer = new Timer(int.Parse(command[2]));
                        var move2 = bot.Think(new Board(board), timer);
                        Console.WriteLine($"bestmove {move2.ToString()[7..^1]}");
                        break;

                    case "quit":
                        return;

                    default:
                        throw new Exception(command[1]);
                }
            }
        }
    }
}
