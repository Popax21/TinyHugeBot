using ChessChallenge.API;
using System;
using System.Collections.Generic;

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
    private static readonly Dictionary<Board, (Move, int, int)> transpositionTable = new Dictionary<Board, (Move, int, int)>(); //Key is ZobristKey, values are stored move, eval and depth

    private static int searchCallIndex = 0;

    private static readonly int[] DeltaPruningPieceValues = { 256, 832, 832, 1344, 2496 };

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

        //Reset the transposition table
        transpositionTable.Clear();
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

            //Some stats, having these lines takes up ~400 bytes though. Make sure to comment these out!
            Console.Write($"Searched to depth {depth} in {timer.MillisecondsElapsedThisTurn:d4}ms, best move eval: {bestEval}");
            if(bestEval == MaxEval) Console.Write($" (forced mate in approx. {1 + depth/2} turns)");
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

        //Check if this is a PV (Principal Variation) node in the search tree
        bool isPVNode = alpha + 1 != beta;

        //Generate the legal moves we can make
        Span<Move> moves = moveBufs[plyIdx];
        board.GetLegalMovesNonAlloc(ref moves);
        
        int numOrderedMoves = 0;

        //Read the transposition table
        (Move, int, int) data;
        if (transpositionTable.TryGetValue(board, out data)) {
            //Check for collisions by seeing if the move is legal
            int index = moves.IndexOf(data.Item1);
            if (index != -1) {
                if (data.Item3 >= depth) {
                    //Use stored value
                    return data.Item2;
                } else {
                    //Bring previous best move to the front
                    Move temp = moves[0];
                    moves[0] = moves[index];
                    moves[index] = temp;
                }
            }
        }

        //Determine the static evaluation of the ply and check if we're improving
        int staticEval = Evaluator.Evaluate(board);
        plyStaticEvals[plyIdx] = staticEval;

        bool improving = plyIdx >= 2 && staticEval > plyStaticEvals[plyIdx-2];

        //Do null move pruning
        if(depth > 0 && !isPVNode && !board.IsInCheck() && staticEval >= beta) {
            //Static null move pruning
            const int StaticNullMovePruningMargin = 256;
            if(depth <= 5 && staticEval >= beta + depth * StaticNullMovePruningMargin) return beta;

            //Non-static null move pruning
            if(depth >= 3) {
                board.ForceSkipTurn();

                int r = 2 + (depth - 2) / 4;
                int? eval = -AlphaBeta(board, depth - r - 2 + (improving ? 1 : 0), -beta, -beta + 1, plyIdx + 1, timer, maxSearchTime);

                board.UndoSkipTurn();

                if(eval >= beta) return eval;
            }
        }

        //Order noisy moves
        numOrderedMoves += MoveOrder.OrderNoisyMoves(moves[numOrderedMoves..]);
        int firstQuietMoveIdx = numOrderedMoves;

        //Determine if we should apply futility pruning to this node
        const int FutilityPruningMarging = 256;
        bool doFutilityPruning = depth <= 5 && !board.IsInCheck() && !isPVNode;
        doFutilityPruning &= staticEval + Math.Max(1, depth + (improving ? 1 : 0)) * FutilityPruningMarging <= alpha;

        //Flag for PVS
        bool firstEval = true;

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

            //Do delta pruning
            if(doFutilityPruning && depth <= 0) {
                const int DeltaPruningBaseBonus = 224, DeltaPruningImprovingBonus = 64;
                //Get piece values for captures and promotions (if applicable)
                int capture = move.IsCapture ? DeltaPruningPieceValues[(int) (move.CapturePieceType - 1)] : 0;
                int promotion = move.IsPromotion ? DeltaPruningPieceValues[(int) (move.PromotionPieceType - 1)] : 0;

                //Ignore move if this new eval is below or equal to alpha
                if(staticEval + capture + promotion + DeltaPruningBaseBonus + (improving ? DeltaPruningImprovingBonus : 0) <= alpha) continue;
            }

            //Store the current state of check for PVS
            bool inCheck = board.IsInCheck();

            //Temporarily make a move
            board.MakeMove(move);

            //Eliminate futile moves that dont check and arent noisy
            if (doFutilityPruning && !board.IsInCheck() && !move.IsCapture && !move.IsPromotion) {
                board.UndoMove(move);
                continue;
            }

            //Evaluate the move recursively with PVS
            int? moveEval;
            if (firstEval || depth <= 0) {
                moveEval = -AlphaBeta(board, depth - 1, -beta, -alpha, plyIdx + 1, timer, maxSearchTime);
            } else {
                int lmrDepth;
                if (depth >= 3 && i >= 3 && beta - alpha == 1 && !move.IsCapture && !move.IsPromotion && !inCheck && !board.IsInCheck()) {
                    lmrDepth = depth - (2 * depth + i) / 8 - 1 + (improving ? 1 : 0);
                    if (lmrDepth < 1) {
                        //History leaf pruning
                        long[] history = board.IsWhiteToMove ? blackHistoryTable : whiteHistoryTable;
                        if (HistoryTable.Get(history, move) < 0) {
                            board.UndoMove(move);
                            continue;
                        }

                        //Set lmrDepth to minimum
                        lmrDepth = 1;
                    }
                } else {
                    lmrDepth = depth - 1;
                }
                //Search using lmr
                moveEval = -AlphaBeta(board, lmrDepth, -alpha - 1, -alpha, plyIdx + 1, timer, maxSearchTime);

                //Re-search the moves normally if necessary
                if (moveEval > alpha && (moveEval < beta || lmrDepth != depth - 1)) {
                    moveEval = -AlphaBeta(board, depth - 1, -beta, -alpha, plyIdx + 1, timer, maxSearchTime);
                }
            }
            firstEval = false;

            //Undo our temporary move
            board.UndoMove(move);

            //This means we ran out of time during searching
            if (moveEval == null) return null;

            //Update the transposition table
            transpositionTable.Remove(board);
            transpositionTable.Add(board, (move, (int)moveEval, depth));

            //Update search variables
            bestEval = Math.Max(bestEval, (int)moveEval);
            alpha = Math.Max(alpha, (int)moveEval);

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
