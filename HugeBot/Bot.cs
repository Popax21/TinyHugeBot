﻿using ChessChallenge.API;

namespace HugeBot;

public class ChessBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();
        return moves[0];
    }
}