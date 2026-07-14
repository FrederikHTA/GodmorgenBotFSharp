module GodmorgenBotFSharp.MongoDb.Mapper

open System
open GodmorgenBotFSharp
open FsToolkit.ErrorHandling

let toDomain (dto : Types.GodmorgenStats) : Result<Domain.GodmorgenStats, Domain.ValidationError> =
    result {
        let! count = Domain.GodmorgenCount.create dto.GodmorgenCount
        let! streak = Domain.GodmorgenStreak.create dto.GodmorgenStreak

        let godmorgenStats : Domain.GodmorgenStats = {
            UserId = Domain.DiscordUserId.create dto.DiscordUserId
            LastGodmorgenDate = dto.LastGoodmorgenDate
            Count = count
            Streak = streak
        }

        return godmorgenStats
    }

let fromDomain (domain : Domain.GodmorgenStats) : Types.GodmorgenStats = {
    Id =
        Types.GodmorgenStats.createMongoId
            (Domain.DiscordUserId.value domain.UserId)
            (DateOnly.FromDateTime domain.LastGodmorgenDate.UtcDateTime)
    DiscordUserId = Domain.DiscordUserId.value domain.UserId
    LastGoodmorgenDate = domain.LastGodmorgenDate
    GodmorgenCount = Domain.GodmorgenCount.value domain.Count
    GodmorgenStreak = Domain.GodmorgenStreak.value domain.Streak
    Year = domain.LastGodmorgenDate.Year
    Month = domain.LastGodmorgenDate.Month
}
