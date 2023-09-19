using System;
using BotTuner.Bots;
using ChessChallenge.API;

//Currently just a placeholder
namespace BotTuner {
    class Program {
        static void Main(string[] args) {
            Board board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            Timer timer = new Timer(60000);
            CSChessBot bot = new CSChessBot("../../../Bots/LittleBlue.cs");
            Console.WriteLine(bot.Think(board, timer));
        }
    }
}
