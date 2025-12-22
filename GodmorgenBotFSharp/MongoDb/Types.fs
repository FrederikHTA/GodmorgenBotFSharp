module GodmorgenBotFSharp.MongoDb.Types

open System
open MongoDB.Bson.Serialization.Attributes

type GodmorgenStats = {
    [<BsonId>]
    Id : string
    DiscordUserId : uint64
    DiscordUsername : string
    LastGoodmorgenDate : System.DateTimeOffset
    GodmorgenCount : int
    GodmorgenStreak : int
    Year : int
    Month : int
}

module GodmorgenStats =
    let createMongoId (userId : uint64) (date : DateTime) : string =
        $"{userId}_{date.Month}_{date.Year}"

    let hasWrittenGodmorgenToday (stats : GodmorgenStats) : bool =
        stats.LastGoodmorgenDate.Date = DateTime.UtcNow.Date

    let increaseGodmorgenCount (stats : GodmorgenStats) : GodmorgenStats = {
        stats with
            LastGoodmorgenDate = DateTimeOffset.UtcNow
            GodmorgenCount = stats.GodmorgenCount + 1
            GodmorgenStreak = stats.GodmorgenStreak + 1
    }

    let create (userId : uint64) (userName : string) : GodmorgenStats =
        let utcNow = DateTime.UtcNow

        {
            Id = createMongoId userId DateTime.Today
            DiscordUserId = userId
            DiscordUsername = userName
            LastGoodmorgenDate = DateTimeOffset.UtcNow
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
    let empty word : WordCount =
        {
            Word = word
            Count = 0
        }
