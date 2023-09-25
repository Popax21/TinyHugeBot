$BuildFlags = @{"p:" = @()}
$BotBuilderFlags = ""

if ($args -match "--validate") {
	$BuildFlags += @{c = "Validate"}
	$BotBuilderFlags += "--debug"
} elseif ($args -match "--tinyvalidate") {
	$BuildFlags += @{c = "Validate"}
} else {
	$BuildFlags += @{c = "Release"}
}

if ($args -match "--bestmove") {
	$BuildFlags["p:"] += "EnableBestMoveDisplay=1"
}

if ($args -match "--fullstats") {
	$BuildFlags["p:"] += "EnableFullStats=1"
} elseif ($args -match "--stats") {
	$BuildFlags["p:"] += "EnableStats=1"
}

dotnet build HugeBot @BuildFlags
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BotBuilderFlags