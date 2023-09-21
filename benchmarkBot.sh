#!/bin/sh -e
./buildTinyBot.sh
dotnet run --project BotTuner -- benchmark TinyBot.cs BotDB