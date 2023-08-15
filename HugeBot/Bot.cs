using ChessChallenge.API;
using HugeBot;

public class MyBot : IChessBot {
    private readonly Searcher searcher = new Searcher();
    public Move Think(Board board, Timer timer) => searcher.SearchMoves(board, timer);
}