#!/bin/sh -e
dotnet build HugeBot
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll