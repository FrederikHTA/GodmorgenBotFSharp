module GodmorgenBotFSharp.Tests.MongoDb.Mapper

open System
open Expecto
open GodmorgenBotFSharp
open GodmorgenBotFSharp.MongoDb

let private dto : Types.GodmorgenStats = {
    Id = "42_1_2024"
    DiscordUserId = 42UL
    DiscordUsername = "test-user"
    LastGoodmorgenDate = DateTimeOffset (2024, 1, 15, 7, 0, 0, TimeSpan.Zero)
    GodmorgenCount = 5
    GodmorgenStreak = 3
    Year = 2024
    Month = 1
}

[<Tests>]
let tests =
    testList "MongoDb.Mapper" [
        testList "toDomain" [
            test "maps all fields correctly" {
                let result = Mapper.toDomain dto
                Expect.equal (Domain.DiscordUserId.value result.UserId) dto.DiscordUserId "UserId"
                Expect.equal (Domain.DiscordUsername.value result.Username) dto.DiscordUsername "Username"
                Expect.equal (Domain.GodmorgenCount.value result.Count) dto.GodmorgenCount "Count"
                Expect.equal (Domain.GodmorgenStreak.value result.Streak) dto.GodmorgenStreak "Streak"
                Expect.equal result.LastGodmorgenDate dto.LastGoodmorgenDate "LastGodmorgenDate"
            }
        ]

        testList "fromDomain roundtrip" [
            test "toDomain >> fromDomain preserves all fields" {
                let domain = Mapper.toDomain dto
                let roundtripped = Mapper.fromDomain domain
                Expect.equal roundtripped.DiscordUserId dto.DiscordUserId "UserId"
                Expect.equal roundtripped.DiscordUsername dto.DiscordUsername "Username"
                Expect.equal roundtripped.GodmorgenCount dto.GodmorgenCount "Count"
                Expect.equal roundtripped.GodmorgenStreak dto.GodmorgenStreak "Streak"
                Expect.equal roundtripped.LastGoodmorgenDate dto.LastGoodmorgenDate "LastGoodmorgenDate"
                Expect.equal roundtripped.Year dto.LastGoodmorgenDate.Year "Year derived from date"
                Expect.equal roundtripped.Month dto.LastGoodmorgenDate.Month "Month derived from date"
            }
        ]
    ]
