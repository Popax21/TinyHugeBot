using System;
using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot : IChessBot {
    private Board searchBoard = null!;
    private Timer searchTimer = null!;
    private int searchAbortTime;

    public Move Think(Board board, Timer timer) {
        //Determine search times
        searchBoard = board;
        searchTimer = timer;
        searchAbortTime = timer.MillisecondsRemaining / 30;

        int deepeningSearchTime = timer.MillisecondsRemaining / 38;

#if STATS
        //Notify the stats tracker that the search starts
        STAT_StartGlobalSearch();
#endif

        //Reset move order tables
        ResetMoveOrderTables_I();

        //Generate all legal moves for later lookup
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        static Move FindMove_I(Span<Move> moves, ushort val) {
            for(int i = 0; i < moves.Length; i++) {
                if(moves[i].RawValue == val) return moves[i];
            }
#if VALIDATE
            throw new Exception($"Search returned invalid root move: 0x{val:x4}");
#else
            return default;
#endif
        }

#if VALIDATE
        string boardFen = board.GetFenString();
#endif

        //Do a NegaMax search with iterative deepening and aspiration windows
#if BESTMOVE || STATS
        int curDepth = -1;
#endif
        int curBestEval = 0;
        ushort curBestMove = 0;
        for(int depth = 1;; depth++) {
#if STATS
            //Notify the stats tracker that the depth search starts
            STAT_StartDepthSearch(depth);
#endif

            //Do a NegaMax search with the current depth
#if BESTMOVE || STATS
            bool didTimeOut = false;
#endif

            ushort iterBestMove = 0;
            try {
                curBestEval = NegaMax(Eval.MinSentinel, Eval.MaxSentinel, depth, 0, out iterBestMove);
                curBestMove = iterBestMove;

#if VALIDATE
                //Check that the board has been properly reset
                if(board.GetFenString() != boardFen) throw new Exception($"Board has not been properly reset after search to depth {depth}: '{boardFen}' != '{board.GetFenString()}'");
            } catch(TimeoutException) {
#else
            } catch(Exception) {
#endif
#if BESTMOVE || STATS
                didTimeOut = true;
#endif

                //Recycle partial search results
                //Note that we can't recycle our evaluation, but that's fine (except for some log output)
                if(iterBestMove != 0) curBestMove = iterBestMove;
            }

#if STATS
            //Notify the stats tracker that the depth search ended
            STAT_EndDepthSearch(iterBestMove != 0 ? FindMove_I(moves, iterBestMove) : default, curBestEval, depth, didTimeOut);
#endif

#if BESTMOVE || STATS
            //Update the current depth
            if(!didTimeOut || iterBestMove != 0) curDepth = depth;
#endif

            //Check if time is up, if we found a checkmate, if we reached our max depth
            if(timer.MillisecondsElapsedThisTurn >= deepeningSearchTime || curBestEval <= Eval.MinMate || curBestEval >= Eval.MaxMate || depth >= MaxDepth) {
                Move bestMove = FindMove_I(moves, curBestMove);

#if STATS
                //Notify the stats tracker that the search ended
                STAT_EndGlobalSearch(bestMove, curBestEval, curDepth);
#endif
#if BESTMOVE
                //Log the best move
                Console.WriteLine($"Searched to depth {curDepth} in {timer.MillisecondsElapsedThisTurn:d5}ms: best move {bestMove.ToString()[7..^1]} eval {(!didTimeOut ? curBestEval.ToString() : "????")}");
#endif
                return bestMove;
            }
        }
    }
}