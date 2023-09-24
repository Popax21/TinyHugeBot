#!/bin/sh -e
BOTBUILDER_FLAGS=
BUILD_FLAGS=

if [[ "$@" == *"--debug"* ]]; then
    BUILD_FLAGS+="-c Debug "
    BOTBUILDER_FLAGS+="--debug "
elif [[ "$@" == *"--tinydebug"* ]]; then
    BUILD_FLAGS+="-c Debug "
else
    BUILD_FLAGS+="-c Release "
fi

if [[ ! "$@" == *"--fullstats"* ]]; then
    BUILD_FLAGS+="-p:DisableFullStats=1 "
    if [[ ! "$@" == *"--stats"* ]]; then
        BUILD_FLAGS+="-p:DisableStats=1 "
    fi
fi

dotnet build HugeBot $BUILD_FLAGS
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BOTBUILDER_FLAGS