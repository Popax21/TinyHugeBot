using ChessChallenge.API;
using System;

namespace HugeBot;

public class Searcher {
    //Use these values to prevent integer overlow
    public const int MinEval = -short.MaxValue, MaxEval = +short.MaxValue;

    public const int MaxPly = 6144, MoveBufSize = 256;

    private static readonly int[] DeltaPruningPieceValues = { 256, 832, 832, 1344, 2496 };

    private readonly int[] moveEvalBuf = new int[MoveBufSize];
    private readonly Move[][] moveBufs = new Move[MaxPly][];
    private readonly int[] plyStaticEvals = new int[MaxPly];
    private readonly long[] whiteHistoryTable = new long[HistoryTable.TableSize], blackHistoryTable = new long[HistoryTable.TableSize];
    private readonly Move[][] killerTables = new Move[MaxPly][];
    private readonly ulong[] transpositionTable = new ulong[TranspositionTable.TableSize];

    public Searcher() {
        //Initialize the move buffers
        for(int i = 0; i < MaxPly; i++) moveBufs[i] = new Move[MoveBufSize];

        //Initialize the killer tables
        for(int i = 0; i < MaxPly; i++) killerTables[i] = new Move[KillerTable.TableSize];
    }

    public Move SearchMoves(Board board, Timer timer) {
        //Determine the amount of time to search for
        int minSearchTime = timer.MillisecondsRemaining / 30;
        int maxSearchTime = minSearchTime + (minSearchTime + timer.IncrementMilliseconds) / 2;

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
                int moveEval = -AlphaBeta(board, depth, MinEval, -bestEval, 1, timer, depth > 0 ? maxSearchTime : int.MaxValue);
                board.UndoMove(move);

                //Check if we ran out of time
                if(moveEval < short.MinValue || moveEval > short.MaxValue) {
                    if(i == 0) {
                        //Get the best move from the previous depth search
                        bestMove = moves[0];
                        bestEval = moveEvalBuf[0];
                    }
                    goto EndSearch;
                }

                moveEvalBuf[i] = moveEval;

                //Update the best move
                if(bestMove.IsNull || bestEval < moveEval) (bestMove, bestEval) = (move, moveEval);
            }

            //Check if we only have one move
            if(moves.Length == 1) goto EndSearch;

            //Check if we ran out of time
            if(timer.MillisecondsElapsedThisTurn < minSearchTime) {
                //We didn't - sort the moves by their evaluation and do another pass
                moveEvalBuf.AsSpan(0, moves.Length).Sort(moves, static (a, b) => -a.CompareTo(b));
                continue;
            }

            EndSearch:;

#if DEBUG
            //Some stats, having these lines takes up ~400 bytes though
            Console.WriteLine($"Searched to depth {depth} in {timer.MillisecondsElapsedThisTurn:d4}ms, best {bestMove.ToString().ToLower()} with eval: {bestEval}");
#endif

            return bestMove;
        }
    }

    public int AlphaBeta(Board board, int depth, int alpha, int beta, int plyIdx, Timer timer, int maxSearchTime) {
        //Check if we ran out of time
        if(timer.MillisecondsElapsedThisTurn >= maxSearchTime) return 1000000;

        //Check if this is triggers the repetition rule
        if(board.IsRepeatedPosition()) return 0;

        //Generate the legal moves we can make
        Span<Move> moves = moveBufs[plyIdx];
        board.GetLegalMovesNonAlloc(ref moves);
        int numOrderedMoves = 0;

        //Check if we're in checkmate or stalemate
        if(moves.Length == 0) return board.IsInCheck() ? MinEval + plyIdx : 0;

        //Check if the 50 move rule triggered
        if(board.IsFiftyMoveDraw()) return 0;

        //Search one move more if we're in check
        if(board.IsInCheck()) depth++;

        //Check if this is a PV (Principal Variation) node in the search tree
        bool isPVNode = alpha + 1 != beta;

        //Check the transposition table
        if(depth > 0 && TranspositionTable.Lookup(transpositionTable, board.ZobristKey, out ushort ttRawMove, out int ttEval, out int ttDepth, out byte ttBound)) {
            //Lookup the move stored in the transposition table in the current legal moves list
            for(int i = 0; i < moves.Length; i++) {
                if(moves[i].RawValue == ttRawMove) {
                    //Swap the move to the front
                    (moves[0], moves[i]) = (moves[i], moves[0]);
                    numOrderedMoves = 1;

                    //Check if there's a TT bound which applies here
                    if(!isPVNode && ttDepth >= depth) {
                        switch(ttBound) {
                            case TTBound.Lower:
                                if(ttEval >= beta) return ttEval;
                                break;
                            case TTBound.Upper:
                                if(ttEval <= alpha) return ttEval;
                                break;
                            case TTBound.Exact: return ttEval;
                        }
                    }

                    break;
                }
            }

            //If we failed to look up the move in the table, decrease the depth if it's high enough
            if(numOrderedMoves < 1 && depth > 5) depth--;
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
#if DEBUG
                if(!board.TrySkipTurn()) throw new Exception("Failed to skip turn for null move pruning!");
#else
                board.ForceSkipTurn();
#endif

                int r = 2 + (depth - 2) / 4;
                int eval = -AlphaBeta(board, depth - r - (improving ? 1 : 2), -beta, -beta + 1, plyIdx + 1, timer, maxSearchTime);

                board.UndoSkipTurn();

                if(eval >= beta) return eval;
            }
        }

        //Order noisy moves
        numOrderedMoves += MoveOrder.OrderNoisyMoves(moves[numOrderedMoves..]);
        int firstQuietMoveIdx = numOrderedMoves;

        //Determine if we should apply futility pruning to this node
        const int FutilityPruningMargin = 256;
        bool doFutilityPruning = depth <= 5 && !board.IsInCheck() && !isPVNode;
        doFutilityPruning &= staticEval + Math.Max(1, depth + (improving ? 1 : 0)) * FutilityPruningMargin <= alpha;

        //Search moves we could make from here
        Move bestMove = default;
        int bestEval = depth <= 0 ? staticEval : MinEval;
        byte evalBound = TTBound.Upper;

        if(bestEval >= beta) return bestEval;
        if(bestEval > alpha) {
            alpha = bestEval;
            evalBound = TTBound.Exact;
        }

        bool wasInCheck = board.IsInCheck();
        for(int i = 0; i < moves.Length; i++) {
            //Check if we've entered the unordered moves
            if(i == numOrderedMoves) {
                if(depth <= 0) break;

                //Sort the moves before proceeding
                numOrderedMoves += MoveOrder.OrderQuietMoves(moves[numOrderedMoves..], board.IsWhiteToMove ? whiteHistoryTable : blackHistoryTable, killerTables[plyIdx]);
            }

            Move move = moves[i];
            bool isNoisyMove = move.IsCapture || move.IsPromotion;

#if DEBUG
            //We only consider captures or promotions as our last moves
            if(depth <= 0 && !isNoisyMove) throw new Exception("Encountered quiet move at depth 0!");
#endif

            //Do delta pruning
            if(doFutilityPruning && depth <= 0) {
                const int DeltaPruningBaseBonus = 224, DeltaPruningImprovingBonus = 64;

                //Get piece values for captures and promotions (if applicable)
                int capture = move.IsCapture ? DeltaPruningPieceValues[(int) move.CapturePieceType - 1] : 0;
                int promotion = move.IsPromotion ? DeltaPruningPieceValues[(int) move.PromotionPieceType - 1] : 0;

                //Ignore move if this new eval is below or equal to alpha
                if(staticEval + capture + promotion + DeltaPruningBaseBonus + (improving ? DeltaPruningImprovingBonus : 0) <= alpha) continue;
            }

            //Temporarily make the move to evaluate it
            board.MakeMove(move);

            //Eliminate futile moves that don't check and aren't noisy
            if(doFutilityPruning && !isNoisyMove && !board.IsInCheck()) {
                board.UndoMove(move);
                continue;
            }

            //Use PVS (Principal Variation Search) if possible
            int moveEval;
            if(depth > 0 && !bestMove.IsNull) {
                //Determine the LMR (Late Move Reduction) depth
                int lmrDepth = depth - 1;
                if(depth >= 3 && i >= 3 && !isPVNode && !isNoisyMove && !wasInCheck && !board.IsInCheck()) {
                    lmrDepth = depth - (2 * depth + i) / 8 - (improving ? 0 : 1);
                    if(lmrDepth < 1) {
                        lmrDepth = 1;

                        //History leaf pruning
                        if(HistoryTable.Get(board.IsWhiteToMove ? blackHistoryTable : whiteHistoryTable, move) < 0) {
                            board.UndoMove(move);
                            continue;
                        }                        
                    }
                }

                //Do the LMR search
                moveEval = -AlphaBeta(board, lmrDepth, -alpha - 1, -alpha, plyIdx+1, timer, maxSearchTime);

                if(moveEval > alpha && (moveEval < beta || lmrDepth != depth-1)) {
                    //Re-search using regular alpha-beta pruning
                    moveEval = -AlphaBeta(board, depth-1, -beta, -alpha, plyIdx+1, timer, maxSearchTime);
                }
            } else {
                //Fall back to simple alpha-beta pruning
                moveEval = -AlphaBeta(board, depth-1, -beta, -alpha, plyIdx+1, timer, maxSearchTime);
            }

            //Undo the temporary move
            board.UndoMove(move);

            //Check if we ran out of time
            if(moveEval < short.MinValue || moveEval > short.MaxValue) return moveEval;

            //Update search variables
            if(moveEval > bestEval) (bestMove, bestEval) = (move, moveEval);

            if(moveEval >= beta) {
                evalBound = TTBound.Lower;

                //Update the history and killer tables if this was a quiet move
                if(isNoisyMove) break;

                long[] historyTable = board.IsWhiteToMove ? whiteHistoryTable : blackHistoryTable;
                KillerTable.OnBetaCutoff(killerTables[plyIdx], move);
                HistoryTable.OnBetaCutoff(historyTable, move, depth);

                //Decrease the history value of all the moves we searched before
                for(int j = firstQuietMoveIdx; j < i; j++) HistoryTable.OnFailedCutoff(historyTable, moves[j], depth);

                break;
            }

            if(moveEval > alpha) {
                alpha = moveEval;
                evalBound = TTBound.Exact;
            }
        }

        //Insert the best move into the TT
        if(!bestMove.IsNull && depth > 0) TranspositionTable.Store(transpositionTable, board.ZobristKey, bestMove, bestEval, depth, evalBound);

        return bestEval;
    }
}
