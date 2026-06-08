module GodmorgenBotFSharp.MongoDb.Mapper

open System
open GodmorgenBotFSharp

let toDomain (dto : Types.GodmorgenStats) : Domain.GodmorgenStats = {
    UserId = Domain.DiscordUserId.create dto.DiscordUserId
    LastGodmorgenDate = dto.LastGoodmorgenDate
    Count = Domain.GodmorgenCount.createUnsafe dto.GodmorgenCount
    Streak = Domain.GodmorgenStreak.createUnsafe dto.GodmorgenStreak
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
