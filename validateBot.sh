#!/bin/sh -e
./buildTinyBot.sh --tinydebug
dotnet run --project BotTuner -- compare TinyBot.cs $@