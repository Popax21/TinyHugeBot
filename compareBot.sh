#!/bin/sh -e
./buildTinyBot.sh
dotnet run --project BotTuner -- compare TinyBot.cs $@