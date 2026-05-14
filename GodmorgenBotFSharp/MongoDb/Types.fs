module GodmorgenBotFSharp.MongoDb.Types

open System
open MongoDB.Bson.Serialization.Attributes
open GodmorgenBotFSharp

type GodmorgenStats = {
    [<BsonId>]
    Id : string
    DiscordUserId : uint64
    DiscordUsername : string
    LastGoodmorgenDate : DateTimeOffset
    GodmorgenCount : int
    GodmorgenStreak : int
    Year : int
    Month : int
}

module GodmorgenStats =
    let createMongoId (userId : uint64) (date : DateOnly) : string =
        $"{userId}_{date.Month}_{date.Year}"

    let create (userId : uint64) (userName : string) : GodmorgenStats =
        let utcNow = DateTimeOffset.UtcNow

        {
            Id = createMongoId userId (DateOnly.FromDateTime utcNow.UtcDateTime)
            DiscordUserId = userId
            DiscordUsername = userName
            LastGoodmorgenDate = utcNow
            GodmorgenCount = 1
            GodmorgenStreak = 1
            Year = utcNow.Year
            Month = utcNow.Month
        }

    let toDomain (dto : GodmorgenStats) : Domain.GodmorgenStats =
        let username = Domain.DiscordUsername.createUnsafe dto.DiscordUsername
        let count = Domain.GodmorgenCount.createUnsafe dto.GodmorgenCount
        let streak = Domain.GodmorgenStreak.createUnsafe dto.GodmorgenStreak

        {
            UserId = Domain.DiscordUserId.create dto.DiscordUserId
            Username = username
            LastGodmorgenDate = dto.LastGoodmorgenDate
            Count = count
            Streak = streak
        }

    let fromDomain (domain : Domain.GodmorgenStats) : GodmorgenStats = {
        Id =
            createMongoId
                (Domain.DiscordUserId.value domain.UserId)
                (DateOnly.FromDateTime domain.LastGodmorgenDate.UtcDateTime)
        DiscordUserId = Domain.DiscordUserId.value domain.UserId
        DiscordUsername = Domain.DiscordUsername.value domain.Username
        LastGoodmorgenDate = domain.LastGodmorgenDate
        GodmorgenCount = Domain.GodmorgenCount.value domain.Count
        GodmorgenStreak = Domain.GodmorgenStreak.value domain.Streak
        Year = domain.LastGodmorgenDate.Year
        Month = domain.LastGodmorgenDate.Month
    }


type WordCount = {
    [<BsonId>]
    Word : string
    Count : int
}

module WordCount =
    let empty word : WordCount = {
        Word = word
        Count = 0
    }

    let create word count : WordCount = {
        Word = word
        Count = count
    }
