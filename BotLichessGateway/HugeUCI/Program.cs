using ChessChallenge.API;
using System;
using System.IO;
using System.Linq;

namespace HugeUCI {
    class Program {

        static MyBot bot;
        static Board board;

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
                        int offset;
                        if (command[1] == "startpos") {
                            board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                            offset = 3;
                        } else {
                            board = Board.CreateBoardFromFEN(string.Join(" ", command[2..8]));
                            offset = 9;
                        }
                        for (; offset < command.Length; offset++) {
                            var move = new Move(command[offset], board);
                            board.MakeMove(move);
                        }
                        break;

                    case "go":
                        var timer = new Timer(int.Parse(command[2]));
                        var move2 = bot.Think(board, timer);
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
