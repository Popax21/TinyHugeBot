using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    readonly int[] pieceValues = {0, 100, 310, 330, 510, 900, 100000};

    
    readonly string[] piecePositions = new string[]
    {
        //Pawn
        "<<<<<<<<><<;;<<>><;BB;<>@=<FF<=@BBBDDBBBHHHHHHHHLLLLLLLL<<<<<<<<",
        //Knight
        ";;;;;;;;;<<<<<<;;<>>>><;;<>@@><;;<>@@><;;<>>>><;;<<<<<<;;;;;;;;;",
        //Bishop
        ";;;;;;;;;><<<<>;<<>>>><<<>>@@>><<>>@@>><<<>>>><<;><<<<>;;;;;;;;;",
        //Rook
        "<;@@@@;<<>>>>>><<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<FFFFFFFFBBBBBBBB",
        //Queen
        "<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<",
        //King
        "@D<<<<D@;;;::;;;888888886666666622222222((((((((((((((((((((((((",
        //King on endgames
        "26::::626:;;;;:6:;<<<<;::;<@@<;::;<@@<;::;<<<<;:6:;;;;:626::::62"
    };
    /*readonly int[,] piecePositions = new int[,]
    {
    {   //Pawn
        0, 0, 0, 0, 0, 0, 0, 0,
        10, 0, 0, -5, -5, 0, 0, 10,
        10, 0, -5, 30, 30, -5, 0, 10,
        20, 5, 0, 50, 50, 0, 5, 20,
        30, 30, 30, 40, 40, 30, 30, 30,
        60, 60, 60, 60, 60, 60, 60, 60,
        80, 80, 80, 80, 80, 80, 80, 80,
        0, 0, 0, 0, 0, 0, 0, 0
    },
    {   //Knight
        -5, -5, -5, -5, -5, -5, -5, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 10, 10, 10, 10, 0, -5,
        -5, 0, 10, 20, 20, 10, 0, -5,
        -5, 0, 10, 20, 20, 10, 0, -5,
        -5, 0, 10, 10, 10, 10, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, -5, -5, -5, -5, -5, -5, -5
    },
    {   //Bishop
        -5, -5, -5, -5, -5, -5, -5, -5,
        -5, 10, 0, 0, 0, 0, 10, -5,
        0, 0, 10, 10, 10, 10, 0, 0,
        0, 10, 10, 20, 20, 10, 10, 0,
        0, 10, 10, 20, 20, 10, 10, 0,
        0, 0, 10, 10, 10, 10, 0, 0,
        -5, 10, 0, 0, 0, 0, 10, -5,
        -5, -5, -5, -5, -5, -5, -5, -5
    },
    {   //Rook
        0, -5, 20, 20, 20, 20, -5, 0,
        0, 10, 10, 10, 10, 10, 10, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 
        0, 0, 0, 0, 0, 0, 0, 0, 
        0, 0, 0, 0, 0, 0, 0, 0, 
        0, 0, 0, 0, 0, 0, 0, 0, 
        50, 50, 50, 50, 50, 50, 50, 50,
        30, 30, 30, 30, 30, 30, 30, 30
    },
    {
        //Queen
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0
    },
    {
        //King
        20, 40, 0, 0, 0, 0, 40, 20,
        -5, -5, -5, -10, -10, -5, -5, -5,
        -20, -20, -20, -20, -20, -20, -20, -20, 
        -30, -30, -30, -30, -30, -30, -30, -30, 
        -50, -50, -50, -50, -50, -50, -50, -50, 
        -100, -100, -100, -100, -100, -100, -100, -100, 
        -100, -100, -100, -100, -100, -100, -100, -100, 
        -100, -100, -100, -100, -100, -100, -100, -100
    },
    {
        //King on endgames
        -50, -30, -10, -10, -10, -10, -30, -50,
        -30, -10, -5, -5, -5, -5, -10, -30,
        -10, -5, 0, 0, 0, 0, -5, -10,
        -10, -5, 0, 20, 20, 0, -5, -10,
        -10, -5, 0, 20, 20, 0, -5, -10,
        -10, -5, 0, 0, 0, 0, -5, -10,
        -30, -10, -5, -5, -5, -5, -10, -30,
        -50, -30, -10, -10, -10, -10, -30, -50
    }
    };*/
    
    int initialDepth;
    Move finalMove = Move.NullMove;

    public Move Think(Board board, Timer timer)
    {
        bool myColor = board.IsWhiteToMove;

        initialDepth = 1;
        int evaluation = 0;
        
        while (timer.MillisecondsElapsedThisTurn < 50)
        {
            evaluation = CalculateMoveEvaluation(board, myColor, initialDepth, -10000000, 10000000);
            initialDepth++;
        }
        
        /*for (int i = 0; i < 7; i++)
        {
            for (int j = 0; j < 64; j++)
            {
                Console.Write((char)(piecePositions[i,j] / 5 + 60));
            }
            Console.Write("\n");
        }*/

        //Console.WriteLine((myColor ? "White: " : "Black: ") + evaluation);

        return finalMove;
    }

    int CalculateMoveEvaluation(Board board, bool color, int depth, int alpha, int beta)
    {
        Move[] moves = board.GetLegalMoves();

        if (depth == 0 || moves.Length == 0)
        {
            return EvaluateBoard(board, color, depth);
        }
        else
        {
            if (board.IsDraw()) return 0;
            if (board.IsInCheckmate()) return -1000000;

            foreach (Move move in moves)
            {
                board.MakeMove(move);

                int evaluation = -CalculateMoveEvaluation(board, !color, depth - 1, -beta, -alpha);

                board.UndoMove(move);

                if (evaluation >= beta) return beta;

                if (evaluation > alpha)
                {
                    alpha = evaluation;
                    if (depth == initialDepth) finalMove = move;
                }

            }

            return alpha;
        }
    }

    int EvaluateBoard(Board board, bool color, int depth)
    {
        if (board.IsDraw()) return 0;
        if (board.IsInCheckmate()) return -1000000 - 100 * depth;

        PieceList[] pieceLists = board.GetAllPieceLists();

        int totalPiecesLeft = 0;
        foreach (PieceList pieceList in pieceLists) totalPiecesLeft += pieceList.Count;

        bool endgame = totalPiecesLeft <= 10;

        int evaluation = 0;

        foreach (PieceList pieceList in pieceLists)
        {
            evaluation += pieceValues[(int)pieceList.TypeOfPieceInList] * pieceList.Count * (pieceList.IsWhitePieceList == color ? 1 : -1);

            for (int i = 0; i < pieceList.Count; i++)
            {
                Piece piece = pieceList.GetPiece(i);

                int positionBonus = (int)piecePositions[(piece.IsKing && endgame) ? 6 : (int)piece.PieceType - 1][piece.IsWhite ? piece.Square.Index : 63 - piece.Square.Index];
                
                evaluation += positionBonus * (piece.IsWhite == color ? 1 : -1);
            }
        }

        evaluation -= ((board.HasKingsideCastleRight(color) || board.HasQueensideCastleRight(color)) ? 0 : 15);
        evaluation += ((board.HasKingsideCastleRight(!color) || board.HasQueensideCastleRight(!color)) ? 0 : 15);

        return evaluation;
    }
}
