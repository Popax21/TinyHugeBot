#!/bin/sh -e
./buildTinyBot.sh $1
pushd Chess-Challenge/Chess-Challenge
cp -f ../../TinyBot.cs "src/My Bot/MyBot.cs"
dotnet run
popd