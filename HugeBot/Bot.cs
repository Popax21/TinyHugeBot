using ChessChallenge.API;
using System;
using HugeBot;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        return Search.SearchMoves(board);
    }
}