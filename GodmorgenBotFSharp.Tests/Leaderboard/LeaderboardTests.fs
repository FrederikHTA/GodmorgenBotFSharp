module GodmorgenBotFSharp.Tests.Leaderboard

open System
open Xunit
open Expecto
open GodmorgenBotFSharp
open GodmorgenBotFSharp.Domain

let private jan = DateTimeOffset (2024, 1, 15, 7, 0, 0, TimeSpan.Zero)
let private feb = DateTimeOffset (2024, 2, 15, 7, 0, 0, TimeSpan.Zero)
let private mar = DateTimeOffset (2024, 3, 15, 7, 0, 0, TimeSpan.Zero)

let private mkStats (userId : uint64) (count : int) (date : DateTimeOffset) : GodmorgenStats = {
    UserId = DiscordUserId.create userId
    LastGodmorgenDate = date
    Count = GodmorgenCount.createUnsafe count
    Streak = GodmorgenStreak.createUnsafe 1
}

[<Fact>]
let ``getCurrentMonthLeaderboard - empty array returns no-one message`` () =
    Expect.equal (Leaderboard.getCurrentMonthLeaderboard [||]) "No one has said godmorgen yet." ""

[<Fact>]
let ``getCurrentMonthLeaderboard - single user shows at rank 1 with correct count`` () =
    let result = Leaderboard.getCurrentMonthLeaderboard [| mkStats 1UL 5 jan |]
    Expect.stringContains result "no: 1" "rank label"
    Expect.stringContains result "<@1>" "user mention"
    Expect.stringContains result "5" "count"

[<Fact>]
let ``getCurrentMonthLeaderboard - users sorted by count descending`` () =
    let stats = [| mkStats 1UL 3 jan ; mkStats 2UL 7 jan |]
    let result = Leaderboard.getCurrentMonthLeaderboard stats
    Expect.isTrue (result.IndexOf "<@2>" < result.IndexOf "<@1>") "higher count user appears first"

[<Fact>]
let ``getCurrentMonthLeaderboard - tied users produce shared-between message`` () =
    let stats = [| mkStats 1UL 5 jan ; mkStats 2UL 5 jan |]
    let result = Leaderboard.getCurrentMonthLeaderboard stats
    Expect.stringContains result "shared between" ""
    Expect.stringContains result "<@1>" ""
    Expect.stringContains result "<@2>" ""

[<Fact>]
let ``getMonthlyLeaderboards - groups entries into one MonthlyRank per month`` () =
    let stats = [| mkStats 1UL 5 jan ; mkStats 2UL 3 jan ; mkStats 1UL 4 feb |]
    Expect.equal (Leaderboard.getMonthlyLeaderboards stats).Length 2 ""

[<Fact>]
let ``getMonthlyLeaderboards - most recent month comes first`` () =
    let stats = [| mkStats 1UL 5 jan ; mkStats 1UL 4 feb |]
    let result = Leaderboard.getMonthlyLeaderboards stats
    Expect.equal result[0].MonthYear.Month 2 "Feb first"
    Expect.equal result[1].MonthYear.Month 1 "Jan second"

[<Fact>]
let ``getMonthlyLeaderboards - rankings within a month sorted by count descending`` () =
    let stats = [| mkStats 1UL 3 jan ; mkStats 2UL 7 jan |]
    let result = Leaderboard.getMonthlyLeaderboards stats
    Expect.stringContains result[0].Rankings[0] "<@2>" "higher count user first"

[<Fact>]
let ``getMonthlyLeaderboards - tied users in a month produce shared-between ranking`` () =
    let stats = [| mkStats 1UL 5 jan ; mkStats 2UL 5 jan |]
    let result = Leaderboard.getMonthlyLeaderboards stats
    Expect.stringContains result[0].Rankings[0] "shared between" ""

[<Fact>]
let ``getOverallRankings - user with most monthly wins is ranked first`` () =
    let stats = [|
        mkStats 1UL 10 jan ; mkStats 2UL 5 jan
        mkStats 2UL 10 feb ; mkStats 1UL 5 feb
        mkStats 1UL 8 mar  ; mkStats 2UL 4 mar
    |]
    let firstLine = (Leaderboard.getOverallRankings stats).Split(Environment.NewLine)[0]
    Expect.stringContains firstLine "<@1>" "user 1 wins 2 months, ranked first"

[<Fact>]
let ``getOverallRankings - equal win counts produce shared-between message`` () =
    let stats = [| mkStats 1UL 10 jan ; mkStats 2UL 10 feb |]
    Expect.stringContains (Leaderboard.getOverallRankings stats) "shared between" ""

[<Fact>]
let ``getOverallRankings - user who never won is ranked below winner`` () =
    let stats = [|
        mkStats 1UL 10 jan ; mkStats 2UL 5 jan
        mkStats 1UL 10 feb ; mkStats 2UL 3 feb
    |]
    let lines = (Leaderboard.getOverallRankings stats).Split(Environment.NewLine)
    Expect.stringContains lines[0] "<@1>" "winner ranked first"
    Expect.stringContains lines[1] "<@2>" "loser ranked second"
