using BotTuner.Factories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChessChallenge.API;

namespace BotTuner {
    class MatchRunner {
        private IChessBotFactory player;
        private IChessBotFactory[] opponents;
        public MatchRunner((string, Dictionary<string, string>?) player, (string, Dictionary<string, string>?)[] opponents) {
            //Initialize the oppponents array
            this.opponents = new IChessBotFactory[opponents.Length];

            //Load each opponent bot on a new thread
            //This is because compiling a MyBot.cs is somewhat slow
            //UCI bots dont need to be loaded in parallel but it wont hurt them
            var threads = new List<Thread>();
            int idx = 0;
            foreach (var opponent in opponents) {
                int idxCopy = idx++;
                var opponentCopy = opponent;
                var thread = new Thread(() => {
                    var bot = LoadBot(opponentCopy);
                    lock(opponents) {
                        this.opponents[idxCopy] = bot;
                    }
                });
                thread.Start();
                threads.Add(thread);
            }

            //Load the player bot
            this.player = LoadBot(player);

            //Wait for all other bots to load
            foreach (var thread in threads) {
                thread.Join();
            }
        }

        public void Test() {
            Board board = Board.CreateBoardFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            var bot = player.Create();
            ChessChallenge.API.Timer timer = new ChessChallenge.API.Timer(60000);
            Console.WriteLine(bot.Think(board, timer));
            foreach (var opponent in opponents) {
                var bot2 = opponent.Create();
                ChessChallenge.API.Timer timer2 = new ChessChallenge.API.Timer(60000);
                Console.WriteLine(bot2.Think(board, timer2));
            }
        }

        private IChessBotFactory LoadBot((string, Dictionary<string, string>?) bot) {
            var (path, options) = bot;

            //If there are no options, then its a MyBot.cs file
            //Otherwise, its a UCI bot
            if (options is null) {
                return new CSChessBotFactory(path);
            } else {
                return new UCIBotFactory(path, options);
            }
        }
    }
}
