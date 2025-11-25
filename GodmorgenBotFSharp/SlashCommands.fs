module GodmorgenBotFSharp.SlashCommands

open System
open System.Threading.Tasks
open GodmorgenBotFSharp.MongoDb.Types
open MongoDB.Driver
open Microsoft.Extensions.Logging
open NetCord.Gateway
open NetCord.Rest
open NetCord.Services.ApplicationCommands

type LeaderboardDelegate = delegate of unit -> Task<string>

let leaderboardCommand (ctx : Context) =
    LeaderboardDelegate (fun _ ->
        task {
            try
                ctx.Logger.LogInformation ("Got leaderboard command request")
                let today = DateTime.UtcNow.Date
                let targetMonth = today.Month
                let targetYear = today.Year

                let filter =
                    Builders<GodmorgenStats>.Filter
                        .And (
                            Builders<GodmorgenStats>.Filter.Eq (_.Year, targetYear),
                            Builders<GodmorgenStats>.Filter.Eq (_.Month, targetMonth)
                        )

                let! statsO = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase

                return
                    match statsO with
                    | None -> "No one has said godmorgen yet this month."
                    | Some stats -> Leaderboard.getCurrentMonthLeaderboard stats
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetCurrentMonthLeaderboard.")
                return "An error occurred while processing your request."
        }
    )

type WordCountDelegate = delegate of NetCord.User * gWord : string * mWord : string -> Task<string>

let wordCountCommand (ctx : Context) =
    WordCountDelegate (fun user gWord mWord ->
        task {
            try
                ctx.Logger.LogInformation ("Got wordcount command request for {User}", user.Username)

                let! wordCountsR = ctx.MongoDataBase |> MongoDb.Functions.getWordCount user gWord mWord

                return
                    match wordCountsR with
                    | Ok wordCounts ->
                        $"The user <@{user.Id}> has used the word {gWord} {wordCounts.gWordCount} times "
                        + $"and the word {mWord} {wordCounts.mWordCount} times."
                    | Error errorValue ->
                        ctx.Logger.LogError (
                            "Failed to get word count for user {User} with error: {Error}",
                            user.Username,
                            errorValue
                        )

                        errorValue
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetWordCount for user {User}.", user.Username)
                return "An error occurred while processing your request."
        }
    )

type GiveUserPointWithWordsDelegate =
    delegate of
        commandContext : ApplicationCommandContext * user : NetCord.User * gWord : string * mWord : string ->
            Task<string>

let giveUserPointWithWordsCommand (ctx : Context) =
    GiveUserPointWithWordsDelegate (fun commandContext user gWord mWord ->
        task {
            ctx.Logger.LogInformation (
                "Got giveuserpointwithwords command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            try
                if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                    return "You are not allowed to use this command, Heretic!"
                else
                    let! giveUserPointResult = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint user
                    let! updateWordCountR = ctx.MongoDataBase |> MongoDb.Functions.updateWordCount user gWord mWord

                    match updateWordCountR with
                    | Ok _ ->
                        return
                            $"User <@{user.Id}> has been given a point from {giveUserPointResult.Previous} to {giveUserPointResult.Current} points!, "
                            + $"and added words: G-word: {gWord}, M-word: {mWord}"
                    | Error errorValue ->
                        ctx.Logger.LogError (
                            "Failed to update word count for user {User} with error: {Error}",
                            user.Username,
                            errorValue
                        )

                        return errorValue
            with ex ->
                ctx.Logger.LogError (ex, "Error in GiveUserPointWithWords for user {User}.", user.Username)
                return "An error occurred while processing your request."
        }
    )

type GiveUserPointDelegate = delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let giveUserPointCommand (ctx : Context) =
    GiveUserPointDelegate (fun commandContext user ->
        task {
            ctx.Logger.LogInformation (
                "Got giveuserpoint command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            try
                if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                    return "You are not allowed to use this command, Heretic!"
                else
                    let! result = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint user

                    return
                        $"User <@{user.Id}> has been given a point from {result.Previous} to {result.Current} points!"
            with ex ->
                ctx.Logger.LogError (ex, "Error in GiveUserPoint for user {User}. ", user.Username)
                return "An error occurred while processing your request."
        }
    )

type RemovePointDelegate = delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let removePointCommand (ctx : Context) =
    RemovePointDelegate (fun commandContext user ->
        task {
            ctx.Logger.LogInformation (
                "Got RemovePoint command command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            try
                if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                    return "You are not allowed to use this command, Heretic!"
                else
                    let! result = ctx.MongoDataBase |> MongoDb.Functions.removeUserPoint user

                    return
                        $"User <@{user.Id}> has had a point removed from {result.Previous} to {result.Current} points!"
            with ex ->
                ctx.Logger.LogError (ex, "Error in RemovePoint for user {User}. ", user.Username)
                return "An error occurred while processing your request."
        }
    )

type TopWordsDelegate = delegate of NetCord.User -> Task<string>

let topWordsCommand (ctx : Context) =
    TopWordsDelegate (fun user ->
        task {
            try
                ctx.Logger.LogInformation ("Got topwords command request for {User}", user.Username)
                let! top5WordsO = ctx.MongoDataBase |> MongoDb.Functions.getTop5Words user

                return
                    match top5WordsO with
                    | Some top5Words when top5Words |> Array.isEmpty |> not ->
                        let wordsFormatted =
                            top5Words
                            |> Array.mapi (fun i wordCount -> $"{i + 1}: {wordCount.Word} - {wordCount.Count}")
                            |> String.concat "\n"

                        $"The top 5 words for <@{user.Id}> are: \n{wordsFormatted}"
                    | _ -> "No words found for the user."
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetWordCount for user: {User}.", user.Username)
                return "An error occurred while processing your request."
        }
    )

type AllTimeLeaderboardDelegate = delegate of unit -> Task<string>

let allTimeLeaderboardCommand (ctx : Context) (gatewayClient : GatewayClient) =
    AllTimeLeaderboardDelegate (fun _ ->
        task {
            try
                ctx.Logger.LogInformation ("Got alltimeleaderboard command request")
                let filter = Builders<GodmorgenStats>.Filter.Empty
                let! godmorgenStatsO = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase

                match godmorgenStatsO with
                | Some godmorgenStats ->
                    let monthlyLeaderboard = Leaderboard.getMonthlyLeaderboard godmorgenStats
                    let overallRankings = Leaderboard.getOverallRankings godmorgenStats

                    let monthlyMessages =
                        monthlyLeaderboard
                        |> Array.sortByDescending (fun x -> x.MonthYear.Year, x.MonthYear.Month)
                        |> Array.take (min 3 monthlyLeaderboard.Length)
                        |> Array.map (fun monthlyRank ->
                            $"Leaderboard for {Leaderboard.abbreviatedMonthName monthlyRank.MonthYear.Month} {monthlyRank.MonthYear.Year}:\n"
                            + String.concat "\n" monthlyRank.Rankings
                        )

                    for monthlyMessage in monthlyMessages do
                        let! _ = gatewayClient.Rest.SendMessageAsync (ctx.DiscordChannelInfo.ChannelId, monthlyMessage)

                        ()

                    return $"Overall Ranking:\n{overallRankings}"
                | None -> return "No one has said godmorgen yet."
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetAllTimeLeaderboard.")
                return "An error occurred while processing your request."
        }
    )
