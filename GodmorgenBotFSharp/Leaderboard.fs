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
    Rankings : string array
}

type private RankGroup = {
    Rank    : int
    Score   : int
    UserIds : DiscordUserId array
}

let private getTrophyEmoji (rank : int) : string =
    match rank with
    | 1 -> ":first_place:"
    | 2 -> ":second_place:"
    | 3 -> ":third_place:"
    | _ -> ":poop:"

let abbreviatedMonthName (month : int) : string =
    DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName month

let private computeRankGroups (scored : (DiscordUserId * int) array) : RankGroup array =
    scored
    |> Array.groupBy (fun (_, score) -> score)
    |> Array.sortByDescending (fun (score, _) -> score)
    |> Array.mapi (fun i (score, group) -> {
        Rank    = i + 1
        Score   = score
        UserIds = group |> Array.map (fun (userId, _) -> userId)
    })

let private formatMentions (userIds : DiscordUserId array) : string array =
    userIds |> Array.map (fun id -> $"<@%d{DiscordUserId.value id}>")

let getCurrentMonthLeaderboard (godmorgenStats : GodmorgenStats array) : string =
    if Array.isEmpty godmorgenStats then
        "No one has said godmorgen yet."
    else
        godmorgenStats
        |> Array.map (fun s -> s.UserId, GodmorgenCount.value s.Count)
        |> computeRankGroups
        |> Array.map (fun group ->
            let mentions = formatMentions group.UserIds
            if group.UserIds.Length = 1 then
                $"The current no: {group.Rank} is {mentions[0]} with a godmorgen count of: {group.Score}"
            else
                let joined = String.concat Environment.NewLine mentions
                $"The current no: {group.Rank} is shared between: {Environment.NewLine}{joined} with a godmorgen count of: {group.Score}"
        )
        |> String.concat Environment.NewLine

let getMonthlyLeaderboards (godmorgenStats : GodmorgenStats array) : MonthlyRank array =
    godmorgenStats
    |> Array.groupBy (fun stat -> stat.LastGodmorgenDate.Year, stat.LastGodmorgenDate.Month)
    |> Array.sortByDescending (fun ((year, month), _) -> year, month)
    |> Array.map (fun ((year, month), monthStats) ->
        let rankings =
            monthStats
            |> Array.map (fun s -> s.UserId, GodmorgenCount.value s.Count)
            |> computeRankGroups
            |> Array.map (fun group ->
                let monthName = abbreviatedMonthName month
                let mentions = formatMentions group.UserIds
                if group.UserIds.Length = 1 then
                    $"The no: {group.Rank} of {monthName} {year} was {mentions[0]} with a godmorgen count of: {group.Score}"
                else
                    let joined = String.concat " + " mentions
                    $"The no: {group.Rank} of {monthName} {year} was shared between: {joined} with a godmorgen count of: {group.Score}"
            )
        {
            MonthYear = { Month = month; Year = year }
            Rankings  = rankings
        }
    )

let getOverallRankings (godmorgenStats : GodmorgenStats array) : string =
    let userWinCount =
        godmorgenStats
        |> Array.groupBy (fun stat -> stat.LastGodmorgenDate.Year, stat.LastGodmorgenDate.Month)
        |> Array.collect (fun (_, group) ->
            let maxCount = group |> Array.map (fun s -> GodmorgenCount.value s.Count) |> Array.max
            group |> Array.map (fun stat ->
                stat.UserId, (if GodmorgenCount.value stat.Count = maxCount then 1 else 0))
        )
        |> Array.groupBy (fun (userId, _) -> userId)
        |> Array.map (fun (userId, wins) -> userId, wins |> Array.sumBy (fun (_, winCount) -> winCount))

    userWinCount
    |> computeRankGroups
    |> Array.map (fun group ->
        let mentions = formatMentions group.UserIds
        if group.UserIds.Length = 1 then
            $"The overall no: {getTrophyEmoji group.Rank} {group.Rank} is {mentions[0]} with {group.Score} month(s) won."
        else
            let joined = String.concat ", " mentions
            $"The overall no: {getTrophyEmoji group.Rank} {group.Rank} is shared between {joined} with {group.Score} month(s) won."
    )
    |> String.concat Environment.NewLine
