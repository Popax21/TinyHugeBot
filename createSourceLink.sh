#!/bin/sh -e
pushd "Chess-Challenge/Chess-Challenge/src/My Bot"
rm -f MyBot.cs
ln -s ../../../../TinyBot.cs MyBot.cs
popd