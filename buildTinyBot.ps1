dotnet build HugeBot -c Release
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $args[0]