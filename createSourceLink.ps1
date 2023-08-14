Push-Location "Chess-Challenge/Chess-Challenge/src/My Bot"
Remove-Item MyBot.cs
New-Item -ItemType SymbolicLink -Path ..\..\..\..\TinyBot.cs -Target MyBot.cs
Pop-Location