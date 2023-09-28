using System;
using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot {
    private const int NumKillerTableSlots = 4;
    private const int NumButterflyIndices = 2 * 8 * 64, NumButterflyBits = 1 + 3 + 6;

    private ushort[] killerTable = new ushort[MaxPlies * NumKillerTableSlots];
    private uint[] historyTable = new uint[NumButterflyIndices], contHistoryTable = new uint[NumButterflyIndices*NumButterflyIndices], followupHistoryTable = new uint[NumButterflyIndices*NumButterflyIndices];
    private uint[] butterflyTable = new uint[NumButterflyIndices];

    private int GetMoveButterflyIndex_I(Move move, bool isWhite) => (isWhite ? 0 : 8*64) | (int) move.MovePieceType << 6 | move.TargetSquare.Index;

    public void MoveOrder_Reset_I() {
        Array.Fill(killerTable, (ushort) 0);
        Array.Fill(historyTable, 0U);
        Array.Fill(contHistoryTable, 0U);
        Array.Fill(followupHistoryTable, 0U);
        Array.Fill(butterflyTable, 1U);
    }

    public int PlaceBestMoveFirst_I(int alpha, int beta, int remDepth, int ply, int searchExtensions, Span<Move> moves, bool ttEntryValid, ulong boardHash) {
#if FSTATS
        STAT_MoveOrder_BestMoveInvoke_I();
#endif

        //Check if TT contains a move
        //Otherwise, if this isn't a PV node (we assume all non-ZW nodes are), potentially use IID (Internal Iterative Deepening)
        ushort bestMove;
        if(ttEntryValid ) {
            //Place the move in the TT first
            bestMove = transposMoveTable[boardHash & TTIdxMask];

#if FSTATS
            STAT_MoveOrder_BestMoveTTHit_I();
#endif
        } else if(beta > alpha-1 && remDepth >= 3) {
            //Perform IID to determine the move to place first
            NegaMax(alpha, beta, remDepth - 2, ply, out bestMove, searchExtensions);

#if FSTATS
            STAT_MoveOrder_BestMoveIIDInvoke_I();
#endif
        } else return 0;

        //Place the best move first
        for(int i = 0; i < moves.Length; i++) {
            if(moves[i].RawValue == bestMove) {
                Move tmp = moves[i];
                moves[i] = moves[0];
                moves[0] = tmp;
                return 1;
            }
        }
        return 0;
    }

    public void SortMoves(Span<Move> moves, int ply) {
        ulong DetermineMoveScore_I(Move move, int ply, bool isWhiteToMove) {
#if FSTATS
            STAT_MoveOrder_ScoreMove_I();
#endif

            if(move.IsCapture | move.IsPromotion) {
#if FSTATS
                STAT_MoveOrder_ScoredNoisyMove_I();
#endif

                //Score by MVV-LAA (Most Valuable Victim - Least Valuable Aggressor)
                //Promotions take priority over other moves
                //TODO Try other metrics (e.g. SSE)
                return (ulong) ((int) move.PromotionPieceType << 6 | (int) move.CapturePieceType << 3 | (7 - (int) move.MovePieceType)) << 55;
            } else {
                //Check if the move is in the killer table
                for(int i = 0; i < NumKillerTableSlots; i++) {
                    if(killerTable[NumKillerTableSlots*ply + i] == move.RawValue) {
#if FSTATS
                        STAT_MoveOrder_ScoredKillerMove_I();
#endif
                        return (ulong) (8 - i) << 52;
                    }
                }

                //Check if this move is a threat escape move
                if(IsThreatEscapeMove_I(move, ply)) {
#if FSTATS
                    STAT_MoveOrder_ScoredThreatEscapeMove_I();
#endif
                    return (ulong) (8 - NumKillerTableSlots) << 52;
                }

                //Return a score based on the Relative History Heuristic
                int moveButterfly = GetMoveButterflyIndex_I(move, searchBoard.IsWhiteToMove);
                ulong moveHistory = historyTable[moveButterfly];
                if(ply > 0) moveHistory += contHistoryTable[(plyMoveButterflies[ply-1] << NumButterflyBits) | moveButterfly];
                if(ply > 1) moveHistory += followupHistoryTable[(plyMoveButterflies[ply-2] << NumButterflyBits) | moveButterfly];
                ulong rhhScore = (moveHistory << 20) / butterflyTable[moveButterfly];

#if VALIDATE
                if(rhhScore >= (1UL << 52)) throw new Exception($"RHH score outside of intended bounds: 0x{rhhScore:x}");
#endif

                return rhhScore;
            }
        }

        //Assign each move a score, then sort by that score
        Span<ulong> moveScores = stackalloc ulong[moves.Length];
        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];
            moveScores[i] = ~DetermineMoveScore_I(move, ply, searchBoard.IsWhiteToMove);
        }
        moveScores.Sort(moves);
    }

    public bool IsMoveQuiet_I(Move move) => !move.IsCapture && !move.IsPromotion;

    public void InsertIntoKillerTable_I(int ply, Move move) {
        ushort moveVal = move.RawValue;

        for(int i = 1; i < NumKillerTableSlots; i++) {
            ushort killerMove = killerTable[NumKillerTableSlots*ply + i];
            if(killerMove == 0 || killerMove == moveVal) {
                //Don't remove the old first move
                killerTable[NumKillerTableSlots*ply + i] = killerTable[NumKillerTableSlots*ply + 0];
                break;
            }
        }

        killerTable[NumKillerTableSlots*ply + 0] = moveVal;
    }

    public void UpdateButterflyTable_I(Move move, bool isWhite, int depth, int ply) {
        int moveButterfly = GetMoveButterflyIndex_I(move, isWhite);
        butterflyTable[moveButterfly]++;
    }

    public void UpdateHistoryTable_I(Move move, bool isWhite, int depth, int ply)  {
        uint incr = (uint) (depth*depth);
        int moveButterfly = GetMoveButterflyIndex_I(move, isWhite);
        historyTable[moveButterfly] += incr; 
        if(ply > 0) contHistoryTable[(plyMoveButterflies[ply-1] << NumButterflyBits) | moveButterfly] += incr;
        if(ply > 1) followupHistoryTable[(plyMoveButterflies[ply-2] << NumButterflyBits) | moveButterfly] += incr;
    }
}