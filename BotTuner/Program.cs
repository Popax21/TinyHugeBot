using System;
using BotTuner.Factories;
using ChessChallenge.API;

//Currently just a placeholder
namespace BotTuner
{
    class Program {
        static void Main(string[] args) {
            Board board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            Timer timer = new Timer(60000);
            IChessBotFactory lbFactory = new CSChessBotFactory("Bots/LittleBlue.cs");
            IChessBot bot = lbFactory.Create();
            Console.WriteLine(bot.Think(board, timer));
        }
    }
}
