using BotTuner.Factories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChessChallenge.API;
using ChessChallenge.Application;

namespace BotTuner {
    class MatchRunner {
        public static void RunMatches(IChessBotFactory player, IChessBotFactory[] opponents, int timerMillis, string[] boards) {
            //Start games for each matchup on a different thread
            var threads = new List<Thread>();
            foreach (var opponent in opponents) {
                var opponentCopy = opponent;
                var thread = new Thread(() => {
                    bool sideToPlay = true;

                    //Loop through all specified boards
                    foreach (var fen in boards) {
                        //Create bot instances
                        var playerInstance = player.Create();
                        var opponentInstance = opponentCopy.Create();

                        //Setup board and timer
                        var board = Board.CreateBoardFromFEN(fen);
                        var timer = new ChessChallenge.API.Timer(timerMillis, timerMillis, timerMillis);

                        //Play moves until the game ends
                        while (!board.IsDraw() && !board.IsInCheckmate() && !(timer.MillisecondsRemaining < 0)) {
                            if (!(board.IsWhiteToMove ^ sideToPlay)) {
                                board.MakeMove(playerInstance.Think(board, timer));
                            } else {
                                board.MakeMove(opponentInstance.Think(board, timer));
                            }

                            //Remake timer, otherwise bots that use the timer will bug
                            timer = new ChessChallenge.API.Timer(timer.OpponentMillisecondsRemaining, timer.MillisecondsRemaining, timerMillis);
                        }

                        //Print out result
                        if (board.IsDraw()) {
                            Console.WriteLine("Ended in draw!");
                        } else if (board.IsWhiteToMove ^ sideToPlay) {
                            Console.WriteLine("Ended in win!");
                        } else {
                            Console.WriteLine("Ended in loss!");
                        }

                        sideToPlay = !sideToPlay;
                    }
                });
                thread.Start();
                threads.Add(thread);
            }

            //Wait for all games to finish
            foreach (var thread in threads) {
                thread.Join();
            }
        }
    }
}
