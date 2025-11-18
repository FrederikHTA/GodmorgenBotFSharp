module GodmorgenBotFSharp.SlashCommands


type PingDelegate = delegate of NetCord.User -> string
let pingCommand = PingDelegate (fun user -> $"Pong! <@{user.Id}>")

type leaderboardDelegate = delegate of NetCord.User -> string
let leaderboardCommand ctx = leaderboardDelegate (fun user -> //
    $"Pong! <@{user.Id}>"
)
