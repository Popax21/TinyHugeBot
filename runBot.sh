#!/bin/sh -e
./buildTinyBot.sh $1
pushd Chess-Challenge/Chess-Challenge
dotnet run
popd