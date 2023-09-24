$BuildFlags = @{}
$BotBuilderFlags = ""

if ($args -match "--debug") {
	$BuildFlags += @{c = "Debug"}
	$BotBuilderFlags += "--debug"
} elseif ($args -match "--tinydebug") {
	$BuildFlags += @{c = "Debug"}
} else {
	$BuildFlags += @{c = "Release"}
}

if (!($args -match "--fullstats")) {
	$BuildFlags += @{"p:" = "DisableFullStats=1"}
	if (!($args -match "--stats")) {
		$BuildFlags += @{"p:" = "DisableStats=1"}
	}
}

dotnet build HugeBot @BuildFlags
dotnet run --project BotBuilder -- HugeBot.dll TinyBot.dll TinyBot.cs $BotBuilderFlags