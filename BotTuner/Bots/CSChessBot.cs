using System;
using ChessChallenge.API;

namespace BotTuner.Bots {
    
    //Takes a MyBot.cs file, compiles it at runtime, and uses that for the bot
    class CSChessBot : IChessBot {
        public CSChessBot() {

        }

        public Move Think(Board board, Timer timer) {
            return Move.NullMove;
        }
    }
}
