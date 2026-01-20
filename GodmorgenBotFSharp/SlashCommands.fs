module GodmorgenBotFSharp.SlashCommands

open System
open System.Threading.Tasks
open GodmorgenBotFSharp.Domain
open GodmorgenBotFSharp.MongoDb.Types
open MongoDB.Driver
open Microsoft.Extensions.Logging
open NetCord.Gateway
open NetCord.Rest
open NetCord.Services.ApplicationCommands
open FsToolkit.ErrorHandling

type LeaderboardDelegate = delegate of unit -> Task<string>

let leaderboardCommand (ctx : Context) =
    LeaderboardDelegate (fun _ ->
        async {
            ctx.Logger.LogInformation "Got leaderboard command request"
            let today = DateTime.UtcNow.Date
            let targetMonth = today.Month
            let targetYear = today.Year

            let filter =
                Builders<GodmorgenStats>.Filter
                    .And (
                        Builders<GodmorgenStats>.Filter.Eq (_.Year, targetYear),
                        Builders<GodmorgenStats>.Filter.Eq (_.Month, targetMonth)
                    )

            let! result = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase

            match result with
            | Some stats -> return Leaderboard.getCurrentMonthLeaderboard stats
            | None -> return "No one has said godmorgen yet this month."
        }
        |> Async.StartAsTask
    )

type WordCountDelegate = delegate of NetCord.User * gWord : string * mWord : string -> Task<string>

let wordCountCommand (ctx : Context) =
    WordCountDelegate (fun user gWordStr mWordStr ->
        async {
            ctx.Logger.LogInformation ("Got wordcount command request for {User}", user.Username)

            // Domain validation
            let gWordResult = GWord.create gWordStr
            let mWordResult = MWord.create mWordStr

            match gWordResult, mWordResult with
            | Ok gWord, Ok mWord ->
                let! wordCounts = MongoDb.Functions.getWordCount user gWord mWord ctx.MongoDataBase

                return
                    $"The user <@{user.Id}> has used the word {gWordStr} {wordCounts.GWord.Count} times "
                    + $"and the word {mWordStr} {wordCounts.MWord.Count} times."
            | Error e, _ -> return $"Invalid G-Word: {e}"
            | _, Error e -> return $"Invalid M-Word: {e}"
        }
        |> Async.StartAsTask
    )

type GiveUserPointWithWordsDelegate =
    delegate of
        commandContext : ApplicationCommandContext *
        user : NetCord.User *
        gWord : string *
        mWord : string ->
            Task<string>

let giveUserPointWithWordsCommand (ctx : Context) =
    GiveUserPointWithWordsDelegate (fun commandContext user gWordStr mWordStr ->
        async {
            ctx.Logger.LogInformation (
                "Got giveuserpointwithwords command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                return "You are not allowed to use this command, Heretic!"
            else
                let gWordResult = GWord.create gWordStr
                let mWordResult = MWord.create mWordStr

                match gWordResult, mWordResult with
                | Ok gWord, Ok mWord ->
                    let! prevAndCurrentGodmorgenCount =
                        MongoDb.Functions.giveUserPoint user ctx.MongoDataBase

                    let! updateWordCountResult =
                        MongoDb.Functions.updateWordCount user gWord mWord ctx.MongoDataBase

                    return
                        $"User <@{user.Id}> has been given a point from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!, "
                        + $"and added words: G-word: {gWordStr}, M-word: {mWordStr}"
                | Error e, _ -> return $"Invalid G-Word: {e}"
                | _, Error e -> return $"Invalid M-Word: {e}"
        }
        |> Async.StartAsTask
    )

type GiveUserPointDelegate =
    delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let giveUserPointCommand (ctx : Context) =
    GiveUserPointDelegate (fun commandContext user ->
        async {
            ctx.Logger.LogInformation (
                "Got giveuserpoint command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                return "You are not allowed to use this command, Heretic!"
            else
                let! prevAndCurrentGodmorgenCount =
                    MongoDb.Functions.giveUserPoint user ctx.MongoDataBase

                return
                    $"User <@{user.Id}> has been given a point from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!"
        }
        |> Async.StartAsTask
    )

type RemovePointDelegate =
    delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let removePointCommand (ctx : Context) =
    RemovePointDelegate (fun commandContext user ->
        async {
            ctx.Logger.LogInformation (
                "Got RemovePoint command command request for {User} requested by {Caller}",
                user.Username,
                commandContext.User.Username
            )

            if commandContext.User.Id <> Constants.PuffyDiscordUserId then
                return "You are not allowed to use this command, Heretic!"
            else
                let! prevAndCurrentGodmorgenCount =
                    MongoDb.Functions.removeUserPoint user ctx.MongoDataBase

                return
                    $"User <@{user.Id}> has had a point removed from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!"
        }
        |> Async.StartAsTask
    )

type TopWordsDelegate = delegate of NetCord.User -> Task<string>

let topWordsCommand (ctx : Context) =
    TopWordsDelegate (fun user ->
        async {
            ctx.Logger.LogInformation ("Got topwords command request for {User}", user.Username)

            let! top5WordsO = MongoDb.Functions.getTop5Words user ctx.MongoDataBase

            match top5WordsO with
            | Some top5Words when not (Array.isEmpty top5Words) ->
                let wordsFormatted =
                    top5Words
                    |> Array.mapi (fun i wordCount ->
                        $"{i + 1}: {wordCount.Word} - {wordCount.Count}"
                    )
                    |> String.concat Environment.NewLine

                return
                    $"The top 5 words for <@{user.Id}> are: {Environment.NewLine}{wordsFormatted}"
            | Some _ -> return "No words found for the user."
            | None ->
                ctx.Logger.LogError "Failed to get top words"
                return "Failed to get top words."
        }
        |> Async.StartAsTask
    )

type AllTimeLeaderboardDelegate = delegate of unit -> Task<string>

let allTimeLeaderboardCommand (ctx : Context) (gatewayClient : GatewayClient) =
    AllTimeLeaderboardDelegate (fun _ ->
        async {
            ctx.Logger.LogInformation "Got alltimeleaderboard command request"
            let filter = Builders<GodmorgenStats>.Filter.Empty

            let! result = MongoDb.Functions.getGodmorgenStats filter ctx.MongoDataBase

            match result with
            | None -> return "No one has said godmorgen yet."
            | Some godmorgenStats ->
                let monthlyLeaderboards = Leaderboard.getMonthlyLeaderboards godmorgenStats
                let overallRankings = Leaderboard.getOverallRankings godmorgenStats

                let monthlyMessages =
                    monthlyLeaderboards
                    |> Array.sortByDescending (fun x -> x.MonthYear.Year, x.MonthYear.Month)
                    |> Array.take (min 3 monthlyLeaderboards.Length)
                    |> Array.map (fun monthlyRank ->
                        let month = Leaderboard.abbreviatedMonthName monthlyRank.MonthYear.Month
                        let year = monthlyRank.MonthYear.Year

                        $"Leaderboard for {month} {year}:{Environment.NewLine}"
                        + String.concat Environment.NewLine monthlyRank.Rankings
                    )

                for monthlyMessage in monthlyMessages do
                    do!
                        gatewayClient.Rest.SendMessageAsync (
                            ctx.DiscordChannelInfo.ChannelId,
                            monthlyMessage
                        )
                        |> Async.AwaitTask
                        |> Async.Ignore

                return $"Overall Ranking:{Environment.NewLine}{overallRankings}"
        }
        |> Async.StartAsTask
    )
