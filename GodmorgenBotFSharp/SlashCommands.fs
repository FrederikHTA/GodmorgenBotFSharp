module GodmorgenBotFSharp.SlashCommands

open System
open System.Threading.Tasks
open GodmorgenBotFSharp.MongoDb.Types
open MongoDB.Driver
open Microsoft.Extensions.Logging
open NetCord.Gateway

type LeaderboardDelegate = delegate of unit -> Task<string>

let leaderboardCommand (ctx : Context) =
    LeaderboardDelegate (fun _ ->
        task {
            try
                let today = DateTime.UtcNow.Date
                let targetMonth = today.Month
                let targetYear = today.Year

                let filter =
                    Builders<GodmorgenStats>.Filter
                        .And (
                            Builders<GodmorgenStats>.Filter.Eq ((fun x -> x.Year), targetYear),
                            Builders<GodmorgenStats>.Filter.Eq ((fun x -> x.Month), targetMonth)
                        )

                let! stats = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase
                return Leaderboard.getCurrentMonthLeaderboard stats
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

                let! wordCounts = ctx.MongoDataBase |> MongoDb.Functions.getWordCount user gWord mWord

                return
                    $"The user <@{user.Id}> has used the word {gWord} {wordCounts.gWordCount} times "
                    + $"and the word {mWord} {wordCounts.mWordCount} times."
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetWordCount for user {User}.", user.Username)
                return "An error occurred while processing your request."
        }
    )

type GiveUserPointWithWordsDelegate = delegate of NetCord.User * gWord : string * mWord : string -> Task<string>

let giveUserPointWithWordsCommand (ctx : Context) =
    GiveUserPointWithWordsDelegate (fun user gWord mWord ->
        task {
            try
                if user.Id <> Constants.PuffyDiscordUserId then
                    return "You are not allowed to use this command, Heretic!"
                else
                    let! result = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint user
                    do! ctx.MongoDataBase |> MongoDb.Functions.updateWordCount user gWord mWord

                    return
                        $"User <@{user.Id}> has been given a point from {result.Previous} to {result.Current} points!, "
                        + $"and added words: G-word: {gWord}, M-word: {mWord}"
            with ex ->
                ctx.Logger.LogError (ex, "Error in GiveUserPointWithWords for user {User}.", user.Username)
                return "An error occurred while processing your request."
        }
    )

type GiveUserPointDelegate = delegate of NetCord.User -> Task<string>

let giveUserPointCommand (ctx : Context) =
    GiveUserPointDelegate (fun user ->
        task {
            try
                if user.Id <> Constants.PuffyDiscordUserId then
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

type TopWordsDelegate = delegate of NetCord.User -> Task<string>

let topWordsCommand (ctx : Context) =
    TopWordsDelegate (fun user ->
        task {
            try
                ctx.Logger.LogInformation ("Got topwords command request for {User}", user.Username)
                let! top5Words = ctx.MongoDataBase |> MongoDb.Functions.getTop5Words user

                if Array.isEmpty top5Words then
                    return "No words found for the user."
                else
                    let top5WordsStringFormat =
                        top5Words |> Array.mapi (fun i wordCount -> $"{i + 1}: {wordCount.Word} - {wordCount.Count}")

                    let message = $"The top 5 words for <@{user.Id}> are: \n"
                    let wordsFormatted = String.concat "\n" top5WordsStringFormat
                    return message + wordsFormatted
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
                let! godmorgenStats = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase

                if Array.isEmpty godmorgenStats then
                    return "No one has said godmorgen yet."
                else
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
                        let! _ = gatewayClient.Rest.SendMessageAsync(ctx.DiscordChannelInfo.ChannelId, monthlyMessage)
                        ()

                    return $"Overall Ranking:\n{overallRankings}"
            with ex ->
                ctx.Logger.LogError (ex, "Error in GetAllTimeLeaderboard.")
                return "An error occurred while processing your request."
        }
    )
