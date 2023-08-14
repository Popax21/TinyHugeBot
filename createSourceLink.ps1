Remove-Item "Chess-Challenge/Chess-Challenge/src/My Bot/MyBot.cs" -ErrorAction Ignore
New-Item -ItemType SymbolicLink -Path TinyBot.cs -Target "Chess-Challenge/Chess-Challenge/src/My Bot/MyBot.cs"