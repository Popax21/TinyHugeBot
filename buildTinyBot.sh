#!/bin/sh -e
BOTBUILDER_FLAGS=
BUILD_FLAGS=

if [[ "$@" == *"--debug" ]]; then
    BUILD_FLAGS+="-c Debug "
    BOTBUILDER_FLAGS+="--debug "
else
    BUILD_FLAGS+="-c Release "
fi

if [[ ! "$@" == *"--stats" ]]; then
    BUILD_FLAGS+="-p:DisableStats=1 "
fi

dotnet build HugeBot $BUILD_FLAGS
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BOTBUILDER_FLAGS