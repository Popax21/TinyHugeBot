#!/bin/sh -e
BOTBUILDER_FLAGS=
BUILD_FLAGS=

if [[ "$@" == *"--validate"* ]]; then
    BUILD_FLAGS+="-c Validate "
    BOTBUILDER_FLAGS+="--debug "
elif [[ "$@" == *"--tinyvalidate"* ]]; then
    BUILD_FLAGS+="-c Validate "
else
    BUILD_FLAGS+="-c Release "
fi

if [[ "$@" == *"--bestmove"* ]]; then
    BUILD_FLAGS+="-p:EnableBestMoveDisplay=1 "
fi

if [[ "$@" == *"--fullstats"* ]]; then
    BUILD_FLAGS+="-p:EnableFullStats=1 "
elif [[ "$@" == *"--stats"* ]]; then
    BUILD_FLAGS+="-p:EnableStats=1 "
fi

dotnet build HugeBot $BUILD_FLAGS
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BOTBUILDER_FLAGS