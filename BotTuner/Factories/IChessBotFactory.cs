using ChessChallenge.API;

namespace BotTuner.Factories {
    interface IChessBotFactory {
        IChessBot Create();
        string GetName();
    }
}
