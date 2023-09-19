using System;
using ChessChallenge.API;

namespace BotTuner.Bots {

    //Takes a MyBot.cs file, compiles it at runtime, and uses that for the bot
    class CSChessBot : IChessBot {

        //Stores the chess bot contained in the source file
        private readonly IChessBot bot;

        public CSChessBot(string path) {

        }

        public Move Think(Board board, Timer timer) => bot.Think(board, timer);
    }
}
