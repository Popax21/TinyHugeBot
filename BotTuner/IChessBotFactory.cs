using ChessChallenge.API;

namespace BotTuner.Factories; 

public interface IChessBotFactory {
    string Name { get; }
    IChessBot CreateBot();
}
