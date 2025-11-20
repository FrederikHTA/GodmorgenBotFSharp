module GodmorgenBotFSharp.MongoDb.Types

open System
open System.Linq.Expressions
open MongoDB.Bson.Serialization.Attributes
open MongoDB.Driver

let mongoDatabaseName = "godmorgen"
let godmorgenStatsCollectionName = "godmorgen_stats"

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

let create (connectionString : string) =
    let mongoClient = new MongoClient (connectionString)
    let database = mongoClient.GetDatabase mongoDatabaseName
    database

let getGodmorgenStats
    (filter : Expression<Func<GodmorgenStats, bool>>)
    (mongoDatabase : IMongoDatabase)
    =
    async {
        let! godmorgenStats =
            mongoDatabase
                .GetCollection<GodmorgenStats>(godmorgenStatsCollectionName)
                .Find(filter)
                .ToListAsync ()
            |> Async.AwaitTask

        return godmorgenStats |> Array.ofSeq
    }

let getHereticUserIds (mongoDatabase : IMongoDatabase) =
    async {
        let collection = mongoDatabase.GetCollection<GodmorgenStats> godmorgenStatsCollectionName
        let today = DateTime.UtcNow.Date
        let currentMonth = today.Month
        let currentYear = today.Year

        let! messages =
            collection.Find (fun msg ->
                msg.Month = currentMonth &&
                msg.Year = currentYear
            )
            |> fun cursor -> cursor.ToListAsync ()
            |> Async.AwaitTask

        return messages
               |> Seq.filter(fun x -> x.LastGoodmorgenDate < DateTimeOffset(today))
               |> Seq.map _.DiscordUserId //
               |> Seq.distinct |> Array.ofSeq
    }
