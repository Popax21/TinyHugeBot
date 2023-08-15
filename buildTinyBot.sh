#!/bin/sh -e
if ["$1" == "--debug"]; then
    dotnet build HugeBot -c Debug
else
    dotnet build HugeBot -c Release
fi
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $1