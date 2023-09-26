#!/bin/sh -e
./buildTinyBot.sh --tinyvalidate
dotnet run --project BotTuner -- compare TinyBot.cs $@