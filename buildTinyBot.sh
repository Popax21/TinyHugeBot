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

if [[ "$@" == *"--fulltstats"* ]]; then
    BUILD_FLAGS+="-p:FullStats=1 "
elif [[ ! "$@" == *"--stats"* ]]; then
    BUILD_FLAGS+="-p:DisableStats=1 "
fi

dotnet build HugeBot $BUILD_FLAGS
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BOTBUILDER_FLAGS