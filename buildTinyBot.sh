#!/bin/sh -e
#We need a clean MyBot.cs for the reference to work
echo "class MyBot:ChessChallenge.API.IChessBot{public ChessChallenge.API.Move Think(ChessChallenge.API.Board b, ChessChallenge.API.Timer t) => b.GetLegalMoves()[0];}" > TinyBot.cs
dotnet build HugeBot -c Release
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $1