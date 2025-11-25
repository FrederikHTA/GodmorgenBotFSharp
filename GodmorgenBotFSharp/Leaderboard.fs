module GodmorgenBotFSharp.Leaderboard

open System
open GodmorgenBotFSharp.MongoDb.Types

type MonthYear = {
    Month : int
    Year : int
}

type MonthlyRank = {
    MonthYear : MonthYear
    Rankings : string array
}

let private getTrophyEmoji (rank : int) : string =
    match rank with
    | 1 -> ":first_place:"
    | 2 -> ":second_place:"
    | 3 -> ":third_place:"
    | _ -> ":poop:"

let abbreviatedMonthName (month : int) : string =
    Globalization.DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName (month)

let getOverallRankings (godmorgenStats : GodmorgenStats array) : string =
    let userWinCount =
        godmorgenStats
        |> Array.groupBy (fun stat -> stat.Year, stat.Month)
        |> Array.collect (fun (_, group) ->
            let maxCount = group |> Array.map _.GodmorgenCount |> Array.max
            group |> Array.map (fun stat -> stat.DiscordUserId, (if stat.GodmorgenCount = maxCount then 1 else 0))
        )
        |> Array.groupBy fst
        |> Array.map (fun (userId, wins) -> userId, wins |> Array.sumBy snd)

    let overallRanking =
        userWinCount
        |> Array.sortByDescending snd
        |> Array.groupBy snd
        |> Array.mapi (fun i (winCount, group) ->
            let rank = i + 1
            let userMentions = group |> Array.map (fun (userId, _) -> $"<@%d{userId}>")

            if group.Length = 1 then
                $"The overall no: {getTrophyEmoji rank} {rank} is {userMentions[0]} with {winCount} month(s) won."
            else
                let concatenatedMentions = String.concat ", " userMentions
                $"The overall no: {getTrophyEmoji rank} {rank} is shared between {concatenatedMentions} with {winCount} month(s) won."
        )

    String.concat "\n" overallRanking

let getMonthlyLeaderboard (godmorgenStats : GodmorgenStats array) : MonthlyRank array =
    godmorgenStats
    |> Array.groupBy (fun stat -> stat.Year, stat.Month)
    |> Array.sortByDescending (fun ((year, month), _) -> year, month)
    |> Array.map (fun ((year, month), monthStats) ->
        let monthRanking =
            monthStats
            |> Array.groupBy (fun s -> s.GodmorgenCount)
            |> Array.sortByDescending fst
            |> Array.mapi (fun i (count, scoreGroup) ->
                let monthName = abbreviatedMonthName month

                if scoreGroup.Length = 1 then
                    $"The no: {i + 1} of {monthName} {year} was <@{scoreGroup[0].DiscordUserId}> with a godmorgen count of: {count}"
                else
                    let userMentions =
                        scoreGroup |> Array.map (fun y -> $"<@%d{y.DiscordUserId}>") |> String.concat " + "

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
        |> Array.groupBy _.GodmorgenCount
        |> Array.sortByDescending fst
        |> Array.mapi (fun i (count, group) ->
            if group.Length = 1 then
                let userMention = $"<@%d{group[0].DiscordUserId}>"
                $"The current no: {i + 1} is {userMention} with a godmorgen count of: {count}"
            else
                let userMentions = group |> Array.map (fun y -> $"<@%d{y.DiscordUserId}>") |> String.concat "\n"
                $"The current no: {i + 1} is shared between: \n{userMentions} with a godmorgen count of: {count}"
        )
        |> String.concat "\n"
