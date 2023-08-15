./buildTinyBot.ps1 $args[0]
Push-Location Chess-Challenge/Chess-Challenge
Copy-Item -Path ../../TinyBot.cs -Destination "src/My Bot/MyBot.cs" -Recurse -Force
dotnet run
Pop-Location