module GodmorgenBotFSharp.Tests.MongoDb.Mapper

open System
open Xunit
open Expecto
open GodmorgenBotFSharp
open GodmorgenBotFSharp.MongoDb

let private dto : Types.GodmorgenStats = {
    Id = "42_1_2024"
    DiscordUserId = 42UL
    LastGoodmorgenDate = DateTimeOffset (2024, 1, 15, 7, 0, 0, TimeSpan.Zero)
    GodmorgenCount = 5
    GodmorgenStreak = 3
    Year = 2024
    Month = 1
}

[<Fact>]
let ``toDomain - maps all fields correctly`` () =
    let result = Mapper.toDomain dto
    Expect.equal (Domain.DiscordUserId.value result.UserId) dto.DiscordUserId "UserId"
    Expect.equal (Domain.GodmorgenCount.value result.Count) dto.GodmorgenCount "Count"
    Expect.equal (Domain.GodmorgenStreak.value result.Streak) dto.GodmorgenStreak "Streak"
    Expect.equal result.LastGodmorgenDate dto.LastGoodmorgenDate "LastGodmorgenDate"

[<Fact>]
let ``toDomain >> fromDomain - roundtrip preserves all fields`` () =
    let roundtripped = dto |> Mapper.toDomain |> Mapper.fromDomain
    Expect.equal roundtripped.DiscordUserId dto.DiscordUserId "UserId"
    Expect.equal roundtripped.GodmorgenCount dto.GodmorgenCount "Count"
    Expect.equal roundtripped.GodmorgenStreak dto.GodmorgenStreak "Streak"
    Expect.equal roundtripped.LastGoodmorgenDate dto.LastGoodmorgenDate "LastGoodmorgenDate"
    Expect.equal roundtripped.Year dto.LastGoodmorgenDate.Year "Year derived from date"
    Expect.equal roundtripped.Month dto.LastGoodmorgenDate.Month "Month derived from date"
