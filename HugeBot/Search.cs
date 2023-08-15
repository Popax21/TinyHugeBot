using ChessChallenge.API;
using System;

namespace HugeBot;

public static class Search {
    //Use these to prevent integer overlow
    public const int MinEval = -int.MaxValue, MaxEval = +int.MaxValue;

    public const int MaxPly = 6144, MoveBufSize = 256;

    private static readonly int[] moveEvalBuf = new int[MoveBufSize];
    private static readonly Move[][] moveBufs = new Move[MaxPly][];
    private static readonly int[] plyStaticEvals = new int[MaxPly];
    private static readonly long[] whiteHistoryTable = new long[HistoryTable.TableSize], blackHistoryTable = new long[HistoryTable.TableSize];
    private static readonly Move[][] killerTables = new Move[MaxPly][];

    private static int searchCallIndex = 0;

    public static void Reset() {
        //Initialize the move buffers
        if(moveBufs[0] == null) {
            for(int i = 0; i < MaxPly; i++) moveBufs[i] = new Move[MoveBufSize];
        }

        //Reset the history tables
        HistoryTable.Reset(whiteHistoryTable);
        HistoryTable.Reset(blackHistoryTable);

        //Reset the killer tables
        if(killerTables[0] == null) {
            for(int i = 0; i < MaxPly; i++) killerTables[i] = new Move[KillerTable.TableSize];
        }
        Array.ForEach(killerTables, KillerTable.Reset);
    }

    public static Move SearchMoves(Board board, Timer timer) {
        //Determine the amount of time to search for
        int minSearchTime = timer.MillisecondsRemaining / 80;
        int maxSearchTime = 2*minSearchTime + timer.IncrementMilliseconds / 2;

        //Determine the initial ply static evaluation
        plyStaticEvals[0] = Evaluator.Evaluate(board);

        //Generate all legal moves
        Span<Move> moves = moveBufs[0];
        board.GetLegalMovesNonAlloc(ref moves);

        //Iteratively search to deeper depths for the best move
        for(int depth = 0;; depth++) {
            Move bestMove = Move.NullMove;
            int bestEval = MinEval;
            for(int i = 0; i < moves.Length; i++) {
                Move move = moves[i];

                //Use alpha-beta pruning to evaluate the move
                board.MakeMove(move);
                int? moveEval = -AlphaBeta(board, depth, MinEval, -bestEval, 1, timer, depth > 0 ? maxSearchTime : int.MaxValue);
                board.UndoMove(move);

                //Check if we ran out of time
                if(!moveEval.HasValue) {
                    if(i == 0) {
                        //Get the best move from the previous depth search
                        bestMove = move;
                        bestEval = moveEvalBuf[0];
                    }
                    goto EndSearch;
                }

                moveEvalBuf[i] = moveEval.Value;

                //Update the best move
                if(bestEval < moveEval) (bestMove, bestEval) = (move, moveEval.Value);

                //Check if we have a forced mate
                if(bestEval == MaxEval) goto EndSearch;
            }

            //Check if we ran out of time
            if(timer.MillisecondsElapsedThisTurn < minSearchTime) {
                //Sort the moves by their evaluation
                moveEvalBuf.AsSpan(0, moves.Length).Sort(moves, static (a, b) => -a.CompareTo(b));
                continue;
            }

            EndSearch:;
            Console.Write($"Searched to depth {depth} in {timer.MillisecondsElapsedThisTurn:d4}ms, best move eval: {bestEval}");
            if(bestEval == MaxEval) Console.Write($" (forced mate in approx. {1 + (depth+1)/2})");
            Console.WriteLine();
            return bestMove;
        }
    }

    public static int? AlphaBeta(Board board, int depth, int alpha, int beta, int plyIdx, Timer timer, int maxSearchTime) {
        //Check if we ran out of time
        if(searchCallIndex % 4096 == 0 && timer.MillisecondsElapsedThisTurn >= maxSearchTime) return null;
        searchCallIndex++;

        //Check if we're in checkmate or have drawn
        if(board.IsInCheckmate()) return MinEval;
        if(board.IsDraw()) return 0;

        //Search one move more if we're in check
        if(board.IsInCheck()) depth++;

        //Check if we reached the bottom of our search
        if(depth == 0) return Evaluator.Evaluate(board);

        //Generate the legal moves we can make
        Span<Move> moves = moveBufs[plyIdx];
        board.GetLegalMovesNonAlloc(ref moves);
        
        int numOrderedMoves = 0;

        //TODO: Transposition table

        //Determine the static evaluation of the ply and check if we're improving
        int staticEval = Evaluator.Evaluate(board);
        plyStaticEvals[plyIdx] = staticEval;

        bool improving = plyIdx >= 2 && staticEval > plyStaticEvals[plyIdx-2];

        //TODO: Null move pruning

        //Order noisy moves
        numOrderedMoves += MoveOrder.OrderNoisyMoves(moves[numOrderedMoves..]);

        //TODO: Futility pruning

        //By now we will have ordered all non-quiet moves
        int firstQuietMoveIdx = numOrderedMoves;

        //Search moves we could make from here
        int bestEval = depth <= 0 ? staticEval : MinEval;
        if(bestEval >= beta) return bestEval;
        if(bestEval > alpha) alpha = bestEval;

        for(int i = 0; i < moves.Length; i++) {
            //Check if we've entered the unordered moves
            if(i >= numOrderedMoves) {
                if(depth <= 0) break;

                //Sort the moves before proceeding
                numOrderedMoves += MoveOrder.OrderQuietMoves(moves[numOrderedMoves..], board.IsWhiteToMove ? whiteHistoryTable : blackHistoryTable, killerTables[plyIdx]);
            }

            Move move = moves[i];

            //We only consider captures or promotions as our last moves
            if(depth <= 0) {
                if(!move.IsCapture && !move.IsPromotion) throw new Exception("Encountered quiet move at depth 0!");
            }

            //TODO: Delta pruning
            //TODO: PVS

            //Evaluate the move recursively
            board.MakeMove(move);
            int? moveEval = -AlphaBeta(board, depth-1, -beta, -alpha, plyIdx+1, timer, maxSearchTime);
            board.UndoMove(move);

            if(!moveEval.HasValue) return null;

            //Update search variables
            bestEval = Math.Max(bestEval, moveEval.Value);
            alpha = Math.Max(alpha, moveEval.Value);

            if(moveEval >= beta) {
                //Update the history and killer tables if this was a quiet move
                if(move.IsCapture || move.IsPromotion) break;

                long[] historyTable = board.IsWhiteToMove ? whiteHistoryTable : blackHistoryTable;
                KillerTable.OnBetaCutoff(killerTables[plyIdx], move);
                HistoryTable.OnBetaCutoff(historyTable, move, depth);

                //Decrease the history value of all the moves we searched before
                for(int j = firstQuietMoveIdx; j < i; j++) HistoryTable.OnFailedCutoff(historyTable, moves[j], depth);

                break;
            }
        }
        return bestEval;
    }
}
