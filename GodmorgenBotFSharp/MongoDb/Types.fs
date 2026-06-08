module GodmorgenBotFSharp.MongoDb.Types

open System
open MongoDB.Bson.Serialization.Attributes

type GodmorgenStats = {
    [<BsonId>]
    Id : string
    DiscordUserId : uint64
    LastGoodmorgenDate : DateTimeOffset
    GodmorgenCount : int
    GodmorgenStreak : int
    Year : int
    Month : int
}

module GodmorgenStats =
    let createMongoId (userId : uint64) (date : DateOnly) : string =
        $"{userId}_{date.Month}_{date.Year}"

    let create (userId : uint64) : GodmorgenStats =
        let utcNow = DateTimeOffset.UtcNow

        {
            Id = createMongoId userId (DateOnly.FromDateTime utcNow.UtcDateTime)
            DiscordUserId = userId
            LastGoodmorgenDate = utcNow
            GodmorgenCount = 1
            GodmorgenStreak = 1
            Year = utcNow.Year
            Month = utcNow.Month
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

type Vacation = {
    [<BsonId>]
    Id : string // string representation of DiscordUserId
    DiscordUserId : uint64
    StartDate : DateTimeOffset
    EndDate : DateTimeOffset
}

type PreviousAndCurrentGodmorgenCount = {
    Previous : int
    Current : int
}

type WordCounts = {
    GWord : WordCount
    MWord : WordCount
}
