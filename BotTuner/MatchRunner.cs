using BotTuner.Factories;
using ChessChallenge.Chess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using API = ChessChallenge.API;

namespace BotTuner;

public class MatchRunner : IDisposable {
    public enum MatchResult {
        Win, Draw, Loss
    }

    public record Match(ulong ID, IChessBotFactory WhiteBot, IChessBotFactory BlackBot, string StartFEN, int TimerStartMs, int TimerIncMs) {
        public readonly TaskCompletionSource<GameResult> CompletionSource = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GameResult> Task => CompletionSource.Task;
    }

    private readonly object botCreateLock = new object();
    private readonly CancellationTokenSource cancelSrc = new CancellationTokenSource();
    private readonly Thread[] runnerThreads;
    private readonly BlockingCollection<Match> matchQueue = new BlockingCollection<Match>();
    private ulong nextMatchID = 0;

    public MatchRunner(int? numRunners = null) {
        numRunners ??= Environment.ProcessorCount / 2;

        //Start runner threads
        runnerThreads = new Thread[numRunners.Value];
        for (int i = 0; i < runnerThreads.Length; i++) {
            runnerThreads[i] = new Thread(RunnerThreadFunc) { IsBackground = true };
            runnerThreads[i].Start();
        }

        Console.WriteLine($"Started {numRunners.Value} match runner threads");
    }

    public void Dispose() {
        //Stop all threads
        cancelSrc.Cancel();
        matchQueue.CompleteAdding();
        Array.ForEach(runnerThreads, t => t.Join());

        cancelSrc.Dispose();
        matchQueue.Dispose();
    }

    private void RunnerThreadFunc() {
        var cancelToken = cancelSrc.Token;
        try {
            while (!cancelToken.IsCancellationRequested) {
                //Dequeue a match
                var match = matchQueue.Take(cancelToken);

                try {
                    //Run the match
                    var matchRes = RunMatch(match, cancelToken);

                    //Report back the result
                    match.CompletionSource.SetResult(matchRes);
                } catch (Exception e) {
                    if (e is OperationCanceledException oe && oe.CancellationToken == cancelToken)
                        throw;
                    match.CompletionSource.SetException(e);
                }
            }
        } catch (OperationCanceledException e) {
            if (e.CancellationToken != cancelToken)
                throw;
        }
    }

    private GameResult RunMatch(Match match, CancellationToken cancelToken) {
        //Setup board and timer
        var board = new Board(null);
        board.LoadPosition(match.StartFEN);

        //Create bot instances
        API.IChessBot whiteBot, blackBot;
        lock(botCreateLock) {
            whiteBot = match.WhiteBot.CreateBot();
            blackBot = match.BlackBot.CreateBot();
        }

        int whiteRemTime = match.TimerStartMs, blackRemTime = match.TimerStartMs;

        //Play moves until the game ends
        var moveGen = new MoveGenerator();
        while (Arbiter.GetGameState(board) == GameResult.InProgress && whiteRemTime > 0 && blackRemTime > 0) {
            cancelToken.ThrowIfCancellationRequested();

            //Let the bot think
            var botBoard = new API.Board(board);
            var botTimer = new API.Timer(board.IsWhiteToMove ? whiteRemTime : blackRemTime, board.IsWhiteToMove ? blackRemTime : whiteRemTime, match.TimerStartMs, match.TimerIncMs);
            var botMove = board.IsWhiteToMove ? whiteBot.Think(botBoard, botTimer) : blackBot.Think(botBoard, botTimer);
            
            (board.IsWhiteToMove ? ref whiteRemTime : ref blackRemTime) = botTimer.MillisecondsRemaining + match.TimerIncMs;

            //Check that the move is legal, then make it
            var move = new Move(botMove.RawValue);
            if (!moveGen.GenerateMoves(board).ToArray().Any(m => m.Value == move.Value))
                throw new Exception($"Bot '{(board.IsWhiteToMove ? match.WhiteBot : match.BlackBot).Name}' made an illegal move in position {FenUtility.CurrentFen(board)}: {MoveUtility.GetMoveNameUCI(move)}");

            HandleMatchPosition(match, board);
            board.MakeMove(move, false);
        }

        if (whiteRemTime <= 0)
            return GameResult.WhiteTimeout;
        else if (blackRemTime <= 0)
            return GameResult.BlackTimeout;
        else
            return Arbiter.GetGameState(board);
    }

    public async Task RunMatches(IChessBotFactory player, IChessBotFactory[] opponents, string[] startFens, int timerStartMs, int timerIncMs, Action<Match, IChessBotFactory, MatchResult>? matchResCb = null) {
        //Queue matches
        List<Match> matches = new List<Match>();
        foreach (IChessBotFactory opponent in opponents) {
            foreach (string startFen in startFens) {
                foreach (bool isWhite in new[] { false, true }) {
                    Match match = new Match(nextMatchID++, isWhite ? player : opponent, isWhite ? opponent : player, startFen, timerStartMs, timerIncMs);
                    matches.Add(match);
                    matchQueue.Add(match);
                }
            }
        }

        int numMatches = matches.Count;
        Console.WriteLine($"RUNNER> Queued {numMatches} matches");

        //Wait for the matches to complete
        while (matches.Count > 0) {
            await Task.WhenAny(matches.Select(m => m.Task));

            //Collect finished matches
            for (int i = 0; i < matches.Count; i++) {
                Match match = matches[i];
                if (match.Task.Exception != null) throw match.Task.Exception;
                if (!match.Task.IsCompleted) continue;

                MatchResult matchRes = match.Task.Result switch {
                    {} res when Arbiter.IsWhiteWinsResult(res) => match.WhiteBot == player ? MatchResult.Win : MatchResult.Loss,
                    {} res when Arbiter.IsBlackWinsResult(res) => match.BlackBot == player ? MatchResult.Win : MatchResult.Loss,
                    {} res when Arbiter.IsDrawResult(res) => MatchResult.Draw,
                    _ => throw new Exception($"Invalid game result {match.Task.Result}")
                };

                Console.WriteLine($"RUNNER> {match.WhiteBot.Name} (white) vs {match.BlackBot.Name} (black) FEN '{match.StartFEN}' -> {Enum.GetName(matchRes)} [{Enum.GetName(match.Task.Result)}]");
                matchResCb?.Invoke(match, match.WhiteBot == player ? match.BlackBot : match.WhiteBot, matchRes);
                HandleMatchResult(match, player, matchRes);

                //Remove the match from the list of ongoing matches
                matches.RemoveAt(i--);
            }
        }
        Console.WriteLine($"RUNNER> Finished running all {numMatches} matches");
    }

    protected virtual void HandleMatchPosition(Match match, Board board) {}
    protected virtual void HandleMatchResult(Match match, IChessBotFactory player, MatchResult result) {}
}