using ChessChallenge.API;
using System;
using HugeBot;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();
        Console.WriteLine(Eval.MaxEval);
        return moves[0];
    }
}