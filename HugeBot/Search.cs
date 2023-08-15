using ChessChallenge.API;
using System;

using System.Linq;
using System.Xml;

namespace HugeBot;

static class Search
{
    // NOTE: default values shouldnt exist once we are called reset at the start of each match
    public static KillerTable[] ply = Enumerable.Repeat<KillerTable>(new KillerTable(), 6144).ToArray();
    public static HistoryTable[] history = Enumerable.Repeat<HistoryTable>(new HistoryTable(), 2).ToArray();

    public static void Reset()
    {
        ply = new KillerTable[6144];
        history = new HistoryTable[2];
    }

    public static Move SearchMoves(Board board)
    {
        // TODO: dynamic depth and timer
        Move bestMove = Move.NullMove;
        int bestEval = int.MinValue;

        foreach(Move move in board.GetLegalMoves()) {
            //Use alpha-beta pruning to evaluate the move
            board.MakeMove(move);
            int moveEval = -AlphaBeta(board, 4, int.MinValue, int.MaxValue, 0);
            board.UndoMove(move);

            //Check if this move is better
            if(bestEval < moveEval) (bestMove, bestEval) = (move, moveEval);
        }

        return bestMove;
    }

    public static int AlphaBeta(Board board, uint depth, int alpha, int beta, int plyIndex)
    {
        //Check for checkmate or draw
        if (board.IsInCheckmate()) return int.MinValue;
        if (board.IsInStalemate() || board.IsFiftyMoveDraw()) return 0;

        //Check for draw
        if (board.IsDraw()) return 0;

        //Search one move more if we're in check
        if (board.IsInCheck()) depth++;

        //Check if we reached the bottom of our search
        if (depth == 0) return Evaluator.Evaluate(board);

        //Search moves we could make from here
        int maxEval = int.MinValue;

        //Counter for later
        int i = 0;

        //Order moves
        Move[] moves = board.GetLegalMoves();
        int noisy = MoveOrder.OrderNoisyMoves(board, ref moves, 0);
        MoveOrder.OrderQuietMoves(ref moves, noisy, ply[plyIndex], history[board.IsWhiteToMove ? 0 : 1]);
        foreach (Move move in moves)
        {
            //Evaluate the move recursively
            board.MakeMove(move);
            int moveEval = -AlphaBeta(board, depth - 1, -beta, -alpha, plyIndex + 1);
            board.UndoMove(move);

            //Update search variables
            maxEval = Math.Max(maxEval, moveEval);
            alpha = Math.Max(alpha, moveEval);
            if (moveEval >= beta)
            {
                //More updating
                ply[plyIndex].BetaCutoff(move);
                HistoryTable table = history[board.IsWhiteToMove ? 0 : 1];
                table.BetaCutoff(move, depth);
                for (int j = noisy; j < i; j++)
                {
                    table.FailedCutoff(moves[j], depth);
                }
                break;
            }
            i++;
        }
        return maxEval;
    }
}
