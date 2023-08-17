using ChessChallenge.API;
using System;

namespace HugeBot;

public class Searcher {
    //Use these values to prevent integer overlow
    public const int MinEval = short.MinValue, MaxEval = short.MaxValue, CheckmateThreshold = MaxEval / 2;

    public const int MaxPly = 6144, MoveBufSize = 256;

    private static readonly int[] DeltaPruningPieceValues = { 256, 832, 832, 1344, 2496 };

    private readonly int[] moveEvalBuf = new int[MoveBufSize];
    private readonly Move[][] moveBufs = new Move[MaxPly][];
    private readonly int[] plyStaticEvals = new int[MaxPly];
    private readonly long[] whiteHistoryTable = new long[HistoryTable.TableSize], blackHistoryTable = new long[HistoryTable.TableSize];
    private readonly Move[][] killerTables = new Move[MaxPly][];
    private readonly ulong[] transpositionTable = new ulong[TranspositionTable.TableSize];

    private int searchCallIndex = 0;
    public Searcher() {
        //Initialize the move buffers
        for(int i = 0; i < MaxPly; i++) moveBufs[i] = new Move[MoveBufSize];

        //Initialize the killer tables
        for(int i = 0; i < MaxPly; i++) killerTables[i] = new Move[KillerTable.TableSize];
    }

    public Move SearchMoves(Board board, Timer timer) {
        //Determine the amount of time to search for
        int minSearchTime = timer.MillisecondsRemaining / 50;
        int maxSearchTime = 2*minSearchTime + timer.IncrementMilliseconds / 2;

        //Determine the initial ply static evaluation
        plyStaticEvals[0] = Evaluator.Evaluate(board);

        //Generate all legal moves
        Span<Move> moves = moveBufs[0];
        board.GetLegalMovesNonAlloc(ref moves);

        //Iteratively search to deeper depths for the best move
        int alpha = MinEval, beta = MaxEval;
        for(int depth = 1;;) {
            Move bestMove = Move.NullMove;
            int bestEval = MinEval;

            for(int i = 0; i < moves.Length; i++) {
                Move move = moves[i];

                //Use alpha-beta pruning to evaluate the move
                board.MakeMove(move);
                int? moveEval = -AlphaBeta(board, depth, MinEval, -bestEval, 1, timer, depth > 0 ? maxSearchTime : int.MaxValue, true);
                board.UndoMove(move);

                //Check if we ran out of time
                if(!moveEval.HasValue) {
                    if(i == 0) {
                        //Get the best move from the previous depth search
                        bestMove = moves[0];
                        bestEval = moveEvalBuf[0];
                    }
                    goto EndSearch;
                }

                moveEvalBuf[i] = moveEval.Value;

                //Update the best move
                if(bestMove.IsNull || bestEval < moveEval) (bestMove, bestEval) = (move, moveEval.Value);
            }

            //Check if we only have one move
            if(moves.Length == 1) goto EndSearch;

            //Check if we ran out of time
            if(timer.MillisecondsElapsedThisTurn < minSearchTime) {
                //We didn't - sort the moves by their evaluation and do another pass
                moveEvalBuf.AsSpan(0, moves.Length).Sort(moves, static (a, b) => -a.CompareTo(b));

                //Update alpha / beta
                if(bestEval < alpha) alpha -= 200;
                else if(bestEval > beta) beta += 200;
                else {
                    alpha = bestEval - 40;
                    beta = bestEval + 40;
                    depth++;
                }

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

    public int? AlphaBeta(Board board, int depth, int alpha, int beta, int plyIdx, Timer timer, int maxSearchTime, bool doNullPruning) {
        //Check if we ran out of time
        if(searchCallIndex % 4096 == 0 && timer.MillisecondsElapsedThisTurn >= maxSearchTime) return null;
        searchCallIndex++;

        //Check if this is triggers the repetition rule
        if(board.IsRepeatedPosition()) return 0;

        //Search one move more if we're in check
        bool isInCheck = board.IsInCheck();
        if(isInCheck) depth++;

        //Check the transposition table
        if(TranspositionTable.Lookup(transpositionTable, board.ZobristKey, out ushort ttMove, out int ttEval, out int ttDepth, out byte ttBound)) {
            //Check if there's a TT bound which applies here
            if(ttDepth >= depth) {
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
        } else {
            //If we failed to look up the move in the table, decrease the depth if it's high enough
            if(depth > 5) depth--;
        }
        ttBound = TTBound.Upper;

        //Determine the static evaluation of the ply and check if we're improving
        int staticEval = Evaluator.Evaluate(board);

        if(depth <= 0) {
            //Special logic for q-search static evals
            if(staticEval >= beta) return beta;
            if(staticEval > alpha) {
                alpha = staticEval;
                // ttBound = TTBound.Exact;
            }
        }

        bool improving = plyIdx >= 2 && staticEval > plyStaticEvals[plyIdx-2];
        plyStaticEvals[plyIdx] = staticEval;

        //Check if this is a PV (Principal Variation) node in the search tree
        bool isPVNode = alpha + 1 < beta;

        //Do null move pruning
        if(depth > 0 && !isPVNode && !isInCheck && staticEval >= beta && beta < CheckmateThreshold) {
            //Static null move pruning
            const int StaticNullMovePruningMargin = 300;
            if(depth < 7 && staticEval >= beta + depth * StaticNullMovePruningMargin) return staticEval;

            //Non-static null move pruning
            if(depth >= 3 && doNullPruning) {
#if DEBUG
                if(!board.TrySkipTurn()) throw new Exception("Failed to skip turn for null move pruning!");
#else
                board.ForceSkipTurn();
#endif

                int r = 2;// + (depth - 2) / 4;
                int? eval = -AlphaBeta(board, depth - r - (improving ? 1 : 2), -beta, -alpha, plyIdx + 1, timer, maxSearchTime, false);

                board.UndoSkipTurn();

                if(!eval.HasValue || eval >= beta) return eval;
            }
        }

        //Determine if we should apply futility pruning to this node
        const int FutilityPruningMargin = 300;
        bool doFutilityPruning = depth <= 5 && !isPVNode && !isInCheck;
        doFutilityPruning &= staticEval + (Math.Max(depth, 1) + (improving ? 1 : 0)) * FutilityPruningMargin <= alpha;

        //Generate the legal moves we can make
        Span<Move> moves = moveBufs[plyIdx];
        board.GetLegalMovesNonAlloc(ref moves, depth <= 0 && !board.IsInCheck());

        //Check for checkmate or draw (except for when in q-search)
        if(depth > 0 && moves.Length == 0) return isInCheck ? MinEval + plyIdx : 0;

        //Order noisy moves
        int numOrderedMoves = MoveOrder.OrderNoisyMoves(moves, ttMove);
        int firstQuietMoveIdx = numOrderedMoves;

        //Search moves we could make from here
        Move bestMove = default;
        int bestEval = depth <= 0 ? staticEval : MinEval;

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
            if(depth <= 0 && !isNoisyMove) throw new Exception($"Encountered quiet move at depth 0! (i={i} numOrderedMoves={numOrderedMoves})");
#endif

            //Do delta pruning for q-search nodes
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
            bool givesCheck = board.IsInCheck();

            //Eliminate futile moves that don't check and aren't noisy
            if(doFutilityPruning && !isNoisyMove && !givesCheck && !bestMove.IsNull) {
                board.UndoMove(move);
                continue;
            }

            //Use PVS (Principal Variation Search) if possible
            int moveEval;
            if(depth > 0 && !bestMove.IsNull) {
                //Determine the LMR (Late Move Reduction) depth
                int lmrDepth = depth - 1;
                if(depth >= 3 && i >= 6 && !isPVNode && !isNoisyMove && !isInCheck && !givesCheck) {
                    lmrDepth = depth - 3; //(2 * depth + i) / 8 - (improving ? 0 : 1);
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
                int? eval = -AlphaBeta(board, lmrDepth, -alpha - 1, -alpha, plyIdx+1, timer, maxSearchTime, doNullPruning);

                if(eval > alpha && (eval < beta || lmrDepth != depth-1)) {
                    //Re-search using regular alpha-beta pruning
                    eval = -AlphaBeta(board, depth-1, -beta, -alpha, plyIdx+1, timer, maxSearchTime, doNullPruning);
                }

                if(!eval.HasValue) {
                    board.UndoMove(move);
                    return null;
                }
                moveEval = eval.Value;
            } else {
                //Fall back to simple alpha-beta pruning
                int? eval = -AlphaBeta(board, depth-1, -beta, -alpha, plyIdx+1, timer, maxSearchTime, doNullPruning);
                if(!eval.HasValue) {
                    board.UndoMove(move);
                    return null;
                }
                moveEval = eval.Value;
            }

            //Undo the temporary move
            board.UndoMove(move);

            //Update search variables
            if(moveEval > bestEval) (bestMove, bestEval) = (move, moveEval);

            if(moveEval > alpha) {
                alpha = moveEval;
                ttBound = TTBound.Exact;
            }

            if(moveEval >= beta) {
                ttBound = TTBound.Lower;

                //Update the history and killer tables if this was a quiet move
                if(depth <= 0 || isNoisyMove) break;

                long[] historyTable = board.IsWhiteToMove ? whiteHistoryTable : blackHistoryTable;
                KillerTable.OnBetaCutoff(killerTables[plyIdx], move);
                HistoryTable.OnBetaCutoff(historyTable, move, depth);

                //Decrease the history value of all the moves we searched before
                for(int j = firstQuietMoveIdx; j < i; j++) HistoryTable.OnFailedCutoff(historyTable, moves[j], depth);

                break;
            }
        }

        //Insert the best move into the TT
        if(!bestMove.IsNull) TranspositionTable.Store(transpositionTable, board.ZobristKey, bestMove, bestEval, depth, ttBound);

        return bestEval;
    }
}
