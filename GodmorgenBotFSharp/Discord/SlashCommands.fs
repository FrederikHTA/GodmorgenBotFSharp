module GodmorgenBotFSharp.SlashCommands

open System
open System.Threading.Tasks
open GodmorgenBotFSharp.Domain
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway
open NetCord.Services.ApplicationCommands

let private requireAdmin (ctx : ApplicationCommandContext) (f : unit -> Task<string>) =
    if ctx.User.Id = Constants.PuffyDiscordUserId then
        f ()
    else
        Task.FromResult "You are not allowed to use this command, Heretic!"

type LeaderboardDelegate = delegate of unit -> Task<string>

let leaderboardCommand (db : IMongoDatabase) (logger : ILogger) =
    LeaderboardDelegate (fun _ ->
        task {
            logger.LogInformation "Got leaderboard command request"
            let today = DateTime.UtcNow.Date
            let! result = MongoDb.Functions.getStatsByMonth today.Month today.Year db

            match result with
            | Some stats -> return Leaderboard.getCurrentMonthLeaderboard stats
            | None -> return "No one has said godmorgen yet this month."
        }
    )

type WordCountDelegate = delegate of NetCord.User * gWord : string * mWord : string -> Task<string>

let wordCountCommand (db : IMongoDatabase) (logger : ILogger) =
    WordCountDelegate (fun user gWordStr mWordStr ->
        task {
            logger.LogInformation ("Got wordcount command request for {User}", user.Username)

            let gWordResult = GWord.create gWordStr
            let mWordResult = MWord.create mWordStr

            match gWordResult, mWordResult with
            | Ok gWord, Ok mWord ->
                let! wordCounts = MongoDb.Functions.getWordCount user.Id gWord mWord db

                return
                    $"The user <@{user.Id}> has used the word {gWordStr} {wordCounts.GWord.Count} times "
                    + $"and the word {mWordStr} {wordCounts.MWord.Count} times."
            | Error e, _ -> return $"Invalid G-Word: {e}"
            | _, Error e -> return $"Invalid M-Word: {e}"
        }
    )

type GiveUserPointWithWordsDelegate =
    delegate of
        commandContext : ApplicationCommandContext * user : NetCord.User * gWord : string * mWord : string ->
            Task<string>

let giveUserPointWithWordsCommand (db : IMongoDatabase) (logger : ILogger) =
    GiveUserPointWithWordsDelegate (fun commandContext user gWordStr mWordStr ->
        requireAdmin
            commandContext
            (fun () ->
                task {
                    logger.LogInformation (
                        "Got giveuserpointwithwords command request for {User}",
                        user.Username,
                        commandContext.User.Username
                    )

                    let gWordResult = GWord.create gWordStr
                    let mWordResult = MWord.create mWordStr

                    match gWordResult, mWordResult with
                    | Ok gWord, Ok mWord ->
                        let! prevAndCurrentGodmorgenCount = MongoDb.Functions.giveUserPoint user.Id db

                        do! MongoDb.Functions.updateWordCount user.Id gWord mWord db

                        return
                            $"User <@{user.Id}> has been given a point from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!, "
                            + $"and added words: G-word: {gWordStr}, M-word: {mWordStr}"
                    | Error e, _ -> return $"Invalid G-Word: {e}"
                    | _, Error e -> return $"Invalid M-Word: {e}"
                }
            )
    )

type GiveUserPointDelegate = delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let giveUserPointCommand (db : IMongoDatabase) (logger : ILogger) =
    GiveUserPointDelegate (fun commandContext user ->
        requireAdmin
            commandContext
            (fun () ->
                task {
                    logger.LogInformation (
                        "Got giveuserpoint command request for {User} requested by {Caller}",
                        user.Username,
                        commandContext.User.Username
                    )

                    let! prevAndCurrentGodmorgenCount = MongoDb.Functions.giveUserPoint user.Id db

                    return
                        $"User <@{user.Id}> has been given a point from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!"
                }
            )
    )

type RemovePointDelegate = delegate of commandContext : ApplicationCommandContext * NetCord.User -> Task<string>

let removePointCommand (db : IMongoDatabase) (logger : ILogger) =
    RemovePointDelegate (fun commandContext user ->
        requireAdmin
            commandContext
            (fun () ->
                task {
                    logger.LogInformation (
                        "Got RemovePoint command command request for {User} requested by {Caller}",
                        user.Username,
                        commandContext.User.Username
                    )

                    let! prevAndCurrentGodmorgenCount = MongoDb.Functions.removeUserPoint user.Id db

                    return
                        $"User <@{user.Id}> has had a point removed from {prevAndCurrentGodmorgenCount.Previous} to {prevAndCurrentGodmorgenCount.Current} points!"
                }
            )
    )

type TopWordsDelegate = delegate of NetCord.User -> Task<string>

let topWordsCommand (db : IMongoDatabase) (logger : ILogger) =
    TopWordsDelegate (fun user ->
        task {
            logger.LogInformation ("Got topwords command request for {User}", user.Username)

            let! top5WordsO = MongoDb.Functions.getTop5Words user.Id db

            match top5WordsO with
            | Some top5Words when not (Array.isEmpty top5Words) ->
                let wordsFormatted =
                    top5Words
                    |> Array.mapi (fun i wordCount -> $"{i + 1}: {wordCount.Word} - {wordCount.Count}")
                    |> String.concat Environment.NewLine

                return $"The top 5 words for <@{user.Id}> are: {Environment.NewLine}{wordsFormatted}"
            | Some _ -> return "No words found for the user."
            | None ->
                logger.LogError "Failed to get top words"
                return "Failed to get top words."
        }
    )

type SetVacationDelegate =
    delegate of
        commandContext : ApplicationCommandContext * user : NetCord.User * startDate : string * endDate : string ->
            Task<string>

let setVacationCommand (db : IMongoDatabase) (logger : ILogger) =
    SetVacationDelegate (fun commandContext user startDateStr endDateStr ->
        requireAdmin
            commandContext
            (fun () ->
                task {
                    logger.LogInformation ("Got setvacation command for {User}", user.Username)

                    match DateOnly.TryParse startDateStr, DateOnly.TryParse endDateStr with
                    | (true, startDate), (true, endDate) when startDate <= endDate ->
                        do! MongoDb.Functions.upsertVacation user.Id startDate endDate db
                        return $"""Vacation set for <@{user.Id}> from {startDate.ToString("yyyy-MM-dd")} to {endDate.ToString("yyyy-MM-dd")}."""
                    | (false, _), _ -> return "Invalid start date. Use format YYYY-MM-DD."
                    | _, (false, _) -> return "Invalid end date. Use format YYYY-MM-DD."
                    | _ -> return "Start date must be before or equal to end date."
                }
            )
    )

type AllTimeLeaderboardDelegate = delegate of unit -> Task<string>

let allTimeLeaderboardCommand
    (db : IMongoDatabase)
    (logger : ILogger)
    (channelId : uint64)
    (gatewayClient : GatewayClient)
    =
    AllTimeLeaderboardDelegate (fun _ ->
        task {
            logger.LogInformation "Got alltimeleaderboard command request"
            let! result = MongoDb.Functions.getAllStats db

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
                    let! _ = gatewayClient.Rest.SendMessageAsync (channelId, monthlyMessage)
                    ()

                return $"Overall Ranking:{Environment.NewLine}{overallRankings}"
        }
    )
