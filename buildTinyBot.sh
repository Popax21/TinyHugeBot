#!/bin/sh -e
dotnet build HugeBot -c Release
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $1