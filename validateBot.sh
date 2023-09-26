#!/bin/sh -e
./buildTinyBot.sh --validate
dotnet run --project BotTuner -- compare TinyBot.cs $@