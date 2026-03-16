module GodmorgenBotFSharp.Leaderboard

open System
open System.Globalization
open GodmorgenBotFSharp.Domain

type MonthYear = {
    Month : int
    Year : int
}

type MonthlyRank = {
    MonthYear : MonthYear
    Rankings : Array<string>
}

let private getTrophyEmoji (rank : int) : string =
    match rank with
    | 1 -> ":first_place:"
    | 2 -> ":second_place:"
    | 3 -> ":third_place:"
    | _ -> ":poop:"

let abbreviatedMonthName (month : int) : string =
    DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName month

let getOverallRankings (godmorgenStats : Array<GodmorgenStats>) : string =
    let userWinCount =
        godmorgenStats
        |> Array.groupBy (fun stat -> stat.LastGodmorgenDate.Year, stat.LastGodmorgenDate.Month)
        |> Array.collect (fun (_, group) ->
            let maxCount = group |> Array.map (fun s -> GodmorgenCount.value s.Count) |> Array.max

            group
            |> Array.map (fun stat ->
                stat.UserId, (if GodmorgenCount.value stat.Count = maxCount then 1 else 0)
            )
        )
        |> Array.groupBy fst
        |> Array.map (fun (userId, wins) -> userId, wins |> Array.sumBy snd)

    let overallRanking =
        userWinCount
        |> Array.sortByDescending snd
        |> Array.groupBy snd
        |> Array.mapi (fun i (winCount, group) ->
            let rank = i + 1

            let userMentions =
                group |> Array.map (fun (userId, _) -> $"<@%d{DiscordUserId.value userId}>")

            if group.Length = 1 then
                $"The overall no: {getTrophyEmoji rank} {rank} is {userMentions.[0]} with {winCount} month(s) won."
            else
                let concatenatedMentions = String.concat ", " userMentions
                $"The overall no: {getTrophyEmoji rank} {rank} is shared between {concatenatedMentions} with {winCount} month(s) won."
        )

    String.concat Environment.NewLine overallRanking

let getMonthlyLeaderboards (godmorgenStats : Array<GodmorgenStats>) : Array<MonthlyRank> =
    godmorgenStats
    |> Array.groupBy (fun stat -> stat.LastGodmorgenDate.Year, stat.LastGodmorgenDate.Month)
    |> Array.sortByDescending (fun ((year, month), _) -> year, month)
    |> Array.map (fun ((year, month), monthStats) ->
        let monthRanking =
            monthStats
            |> Array.groupBy (fun s -> GodmorgenCount.value s.Count)
            |> Array.sortByDescending fst
            |> Array.mapi (fun i (count, scoreGroup) ->
                let monthName = abbreviatedMonthName month

                if scoreGroup.Length = 1 then
                    $"The no: {i + 1} of {monthName} {year} was <@{DiscordUserId.value scoreGroup.[0].UserId}> with a godmorgen count of: {count}"
                else
                    let userMentions =
                        scoreGroup
                        |> Array.map (fun y -> $"<@%d{DiscordUserId.value y.UserId}>")
                        |> String.concat " + "

                    $"The no: {i + 1} of {monthName} {year} was shared between: {userMentions} with a godmorgen count of: {count}"
            )

        {
            MonthYear = {
                Month = month
                Year = year
            }
            Rankings = monthRanking
        }
    )

let getCurrentMonthLeaderboard (godmorgenStats : GodmorgenStats array) : string =
    if Array.isEmpty godmorgenStats then
        "No one has said godmorgen yet."
    else
        godmorgenStats
        |> Array.groupBy (fun s -> GodmorgenCount.value s.Count)
        |> Array.sortByDescending fst
        |> Array.mapi (fun i (godmorgenCount, godmorgenStatsGrouped) ->
            if godmorgenStatsGrouped.Length = 1 then
                let userMention = $"<@%d{DiscordUserId.value godmorgenStatsGrouped[0].UserId}>"
                $"The current no: {i + 1} is {userMention} with a godmorgen count of: {godmorgenCount}"
            else
                let userMentions =
                    godmorgenStatsGrouped
                    |> Array.map (fun y -> $"<@%d{DiscordUserId.value y.UserId}>")
                    |> String.concat Environment.NewLine

                $"The current no: {i + 1} is shared between: {Environment.NewLine}{userMentions} with a godmorgen count of: {godmorgenCount}"
        )
        |> String.concat Environment.NewLine
