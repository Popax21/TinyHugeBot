using ChessChallenge.API;
using HugeBot;

public class MyBot : IChessBot {
    public MyBot() => Search.Reset();
    public Move Think(Board board, Timer timer) => Search.SearchMoves(board, timer);
}