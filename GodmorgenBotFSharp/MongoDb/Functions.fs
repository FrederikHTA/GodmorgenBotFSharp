module GodmorgenBotFSharp.MongoDb.Functions

open System
open System.Threading
open System.Threading.Tasks
open GodmorgenBotFSharp
open MongoDB.Driver
open FsToolkit.ErrorHandling

[<Literal>]
let mongoDatabaseName : string = "godmorgen"

[<Literal>]
let godmorgenStatsCollectionName : string = "godmorgen_stats"

type PreviousAndCurrentGodmorgenCount = {
    Previous : int
    Current : int
}

type WordCounts = {
    GWord : Types.WordCount
    MWord : Types.WordCount
}

let createDatabase (connectionString : string) : IMongoDatabase =
    let mongoClient = new MongoClient (connectionString)
    mongoClient.GetDatabase mongoDatabaseName

let createUser (mongoDatabase : IMongoDatabase) (userId : uint64) (userName : string) : Task<Types.GodmorgenStats> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let newUser = Types.GodmorgenStats.create userId userName
        do! collection.InsertOneAsync newUser
        return newUser
    }

let getGodmorgenStat (userId : uint64) (mongoDatabase : IMongoDatabase) : Task<Option<Domain.GodmorgenStats>> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId = Types.GodmorgenStats.createMongoId userId (DateOnly.FromDateTime DateTime.UtcNow)

        let! godmorgenStatDto =
            collection.Find(fun x -> x.Id = mongoId).SingleOrDefaultAsync () |> Task.map Option.ofObj

        match godmorgenStatDto with
        | Some dto -> return dto |> Types.GodmorgenStats.toDomain |> Some
        | None -> return None
    }

let getGodmorgenStats
    (filter : FilterDefinition<Types.GodmorgenStats>)
    (mongoDatabase : IMongoDatabase)
    : Task<Option<Array<Domain.GodmorgenStats>>> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatDtos = collection.Find(filter).ToListAsync () |> Task.map Option.ofObj

        match godmorgenStatDtos with
        | Some dtos when dtos.Count > 0 -> return dtos |> Seq.map Types.GodmorgenStats.toDomain |> Seq.toArray |> Some
        | Some _ -> return None
        | None -> return None
    }

let removeUserPoint (user : NetCord.User) (mongoDatabase : IMongoDatabase) : Task<PreviousAndCurrentGodmorgenCount> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatO = getGodmorgenStat user.Id mongoDatabase

        match godmorgenStatO with
        | None ->
            return {
                Previous = 0
                Current = 0
            }
        | Some godmorgenStat ->
            let updatedGodmorgenStats =
                godmorgenStat |> Domain.GodmorgenStats.decreaseGodmorgenCount |> Types.GodmorgenStats.fromDomain

            let mongoId = Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedGodmorgenStats) |> Task.ignore

            return {
                Previous = godmorgenStat.Count |> Domain.GodmorgenCount.value
                Current = updatedGodmorgenStats.GodmorgenCount
            }
    }

let giveUserPoint (user : NetCord.User) (mongoDatabase : IMongoDatabase) : Task<PreviousAndCurrentGodmorgenCount> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatO = getGodmorgenStat user.Id mongoDatabase

        match godmorgenStatO with
        | None ->
            let! newUser = createUser mongoDatabase user.Id user.Username

            return {
                Previous = 0
                Current = newUser.GodmorgenCount
            }
        | Some godmorgenStats ->
            let updatedGodmorgenStats =
                Domain.GodmorgenStats.incrementGodmorgenCount godmorgenStats |> Types.GodmorgenStats.fromDomain

            let mongoId = Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedGodmorgenStats) |> Task.ignore

            return {
                Previous = godmorgenStats.Count |> Domain.GodmorgenCount.value
                Current = updatedGodmorgenStats.GodmorgenCount
            }
    }

let updateWordCount
    (user : NetCord.User)
    (gWord : Domain.GWord)
    (mWord : Domain.MWord)
    (mongoDatabase : IMongoDatabase)
    : Task =
    task {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"
        let options = UpdateOptions (IsUpsert = true)

        let upsertWord (word : string) =
            let filter = Builders<Types.WordCount>.Filter.Eq (_.Word, word)
            let update = Builders<Types.WordCount>.Update.Inc (_.Count, 1)

            collection.UpdateOneAsync (filter, update, options)

        // same as Task.WhenAll([| upsertWord(gWord); upsertWord(mWord) |])
        let! _ = upsertWord (Domain.GWord.value gWord)
        and! _ = upsertWord (Domain.MWord.value mWord)

        return ()
    }

let getHereticUserIds (utcNow : DateOnly) (mongoDatabase : IMongoDatabase) : Task<Array<Domain.DiscordUserId>> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let startOfTodayUtc = utcNow.ToDateTime (TimeOnly.MinValue, DateTimeKind.Utc) |> DateTimeOffset

        let filter =
            Builders<Types.GodmorgenStats>.Filter
                .And (
                    Builders<Types.GodmorgenStats>.Filter.Eq (_.Month, utcNow.Month),
                    Builders<Types.GodmorgenStats>.Filter.Eq (_.Year, utcNow.Year),
                    Builders<Types.GodmorgenStats>.Filter.Lt (_.LastGoodmorgenDate, startOfTodayUtc)
                )

        let field = ExpressionFieldDefinition<Types.GodmorgenStats, uint64> _.DiscordUserId

        let! hereticUserIdsCursor =
            collection.DistinctAsync<uint64> (field, filter, null, CancellationToken.None)
            |> Task.map _.ToList()

        return hereticUserIdsCursor |> Seq.map Domain.DiscordUserId.create |> Array.ofSeq
    }

let getWordCount
    (user : NetCord.User)
    (gWord : Domain.GWord)
    (mWord : Domain.MWord)
    (mongoDatabase : IMongoDatabase)
    : Task<WordCounts> =
    task {
        let gWordVal = Domain.GWord.value gWord
        let mWordVal = Domain.MWord.value mWord
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let filter = Builders<Types.WordCount>.Filter.In (_.Word, [| gWordVal ; mWordVal |])

        let! wordCountsDtos = collection.Find(filter).ToListAsync () |> Task.map Option.ofObj

        let counts =
            match wordCountsDtos with
            | None -> Map.empty
            | Some dtos -> dtos |> Seq.toList |> List.map (fun x -> x.Word.ToLowerInvariant (), x.Count) |> Map.ofList

        let findOrDefault (word : string) =
            counts
            |> Map.tryFind word
            |> Option.map (Types.WordCount.create word)
            |> Option.defaultValue (Types.WordCount.empty word)

        return {
            GWord = findOrDefault gWordVal
            MWord = findOrDefault mWordVal
        }
    }

let getTop5Words (user : NetCord.User) (mongoDatabase : IMongoDatabase) : Task<Option<Array<Types.WordCount>>> =
    task {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let! results =
            collection.Find(fun _ -> true).SortByDescending(_.Count).Limit(5).ToListAsync ()
            |> Task.map Option.ofObj

        return results |> Option.map Seq.toArray
    }

#if DEBUG
open Expecto
open Testcontainers.MongoDb

let private sharedMongoContainer =
    lazy
        (task {
            let image : string = "mongo:7.0"
            let container = MongoDbBuilder image
            let mongoContainer = container.Build ()

            do! mongoContainer.StartAsync ()

            AppDomain.CurrentDomain.ProcessExit.Add (fun _ -> mongoContainer.DisposeAsync().GetAwaiter().GetResult ())

            return mongoContainer
        })

let private withMongoDatabase (runTest : IMongoDatabase -> Task<unit>) : Task<unit> =
    task {
        let! mongoContainer = sharedMongoContainer.Value
        let databaseName : string = $"godmorgen_test_{Guid.NewGuid ():N}"
        let mongoClient = new MongoClient (mongoContainer.GetConnectionString ())
        let database = mongoClient.GetDatabase databaseName

        try
            do! runTest database
        finally
            mongoClient.DropDatabase databaseName
    }

let private seedGodmorgenStats (database : IMongoDatabase) (stats : Array<Types.GodmorgenStats>) : Task<unit> =
    task {
        let collection = database.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName
        do! collection.InsertManyAsync stats
    }

[<Tests>]
let tests =
    testList "MongoDb.Functions tests" [
        yield!
            testFixtureTask withMongoDatabase [
                "getGodmorgenStats returns None when there are no matches",
                (fun database ->
                    task {
                        let filter = Builders<Types.GodmorgenStats>.Filter.Eq (_.DiscordUserId, 123UL)

                        let! actual = getGodmorgenStats filter database

                        Expect.equal actual None "Expected no stats when collection has no matching documents"
                    }
                )
                "getGodmorgenStats maps a stored document to domain",
                (fun database ->
                    task {
                        let nowUtc = DateTimeOffset.UtcNow

                        let dto : Types.GodmorgenStats = {
                            Id = "42_seed"
                            DiscordUserId = 42UL
                            DiscordUsername = "test-user"
                            LastGoodmorgenDate = nowUtc
                            GodmorgenCount = 5
                            GodmorgenStreak = 3
                            Year = nowUtc.Year
                            Month = nowUtc.Month
                        }

                        do! seedGodmorgenStats database [| dto |]

                        let filter = Builders<Types.GodmorgenStats>.Filter.Eq (_.DiscordUserId, 42UL)

                        let! actual = getGodmorgenStats filter database

                        match actual with
                        | None -> failtest "Expected one mapped domain record"
                        | Some stats ->
                            Expect.equal stats.Length 1 "Expected exactly one mapped domain record"

                            let mapped = stats[0]

                            Expect.equal
                                (Domain.DiscordUserId.value mapped.UserId)
                                dto.DiscordUserId
                                "UserId should map"

                            Expect.equal
                                (Domain.DiscordUsername.value mapped.Username)
                                dto.DiscordUsername
                                "Username should map"

                            Expect.equal
                                (Domain.GodmorgenCount.value mapped.Count)
                                dto.GodmorgenCount
                                "Count should map"

                            Expect.equal
                                (Domain.GodmorgenStreak.value mapped.Streak)
                                dto.GodmorgenStreak
                                "Streak should map"
                    }
                )
                "getHereticUserIds returns users who have not written today in current month",
                (fun database ->
                    task {
                        let nowUtc = DateTimeOffset.UtcNow
                        let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
                        let oldDate = nowUtc.AddDays -1.0

                        let staleInCurrentMonth : Types.GodmorgenStats = {
                            Id = "11_stale"
                            DiscordUserId = 11UL
                            DiscordUsername = "stale-user"
                            LastGoodmorgenDate = oldDate
                            GodmorgenCount = 2
                            GodmorgenStreak = 1
                            Year = todayUtc.Year
                            Month = todayUtc.Month
                        }

                        let currentDayUser : Types.GodmorgenStats = {
                            Id = "12_today"
                            DiscordUserId = 12UL
                            DiscordUsername = "today-user"
                            LastGoodmorgenDate = nowUtc
                            GodmorgenCount = 7
                            GodmorgenStreak = 2
                            Year = todayUtc.Year
                            Month = todayUtc.Month
                        }

                        let staleOtherMonth : Types.GodmorgenStats = {
                            Id = "13_other_month"
                            DiscordUserId = 13UL
                            DiscordUsername = "other-month-user"
                            LastGoodmorgenDate = oldDate
                            GodmorgenCount = 4
                            GodmorgenStreak = 1
                            Year = if todayUtc.Month = 1 then todayUtc.Year - 1 else todayUtc.Year
                            Month = if todayUtc.Month = 1 then 12 else todayUtc.Month - 1
                        }

                        do! seedGodmorgenStats database [| staleInCurrentMonth ; currentDayUser ; staleOtherMonth |]

                        let! actual = getHereticUserIds todayUtc database

                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal
                            actualIds
                            (Set.ofList [ 11UL ])
                            "Only stale users in current month should be returned"
                    }
                )
                "getHereticUserIds returns distinct user ids",
                (fun database ->
                    task {
                        let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
                        let oldDate = DateTimeOffset.UtcNow.AddDays -2.0

                        let duplicatedUserA : Types.GodmorgenStats = {
                            Id = "21_dup_a"
                            DiscordUserId = 21UL
                            DiscordUsername = "dup-user"
                            LastGoodmorgenDate = oldDate
                            GodmorgenCount = 1
                            GodmorgenStreak = 1
                            Year = todayUtc.Year
                            Month = todayUtc.Month
                        }

                        let duplicatedUserB : Types.GodmorgenStats = {
                            Id = "21_dup_b"
                            DiscordUserId = 21UL
                            DiscordUsername = "dup-user-second-doc"
                            LastGoodmorgenDate = oldDate
                            GodmorgenCount = 2
                            GodmorgenStreak = 2
                            Year = todayUtc.Year
                            Month = todayUtc.Month
                        }

                        let uniqueUser : Types.GodmorgenStats = {
                            Id = "22_unique"
                            DiscordUserId = 22UL
                            DiscordUsername = "unique-user"
                            LastGoodmorgenDate = oldDate
                            GodmorgenCount = 1
                            GodmorgenStreak = 1
                            Year = todayUtc.Year
                            Month = todayUtc.Month
                        }

                        do! seedGodmorgenStats database [| duplicatedUserA ; duplicatedUserB ; uniqueUser |]

                        let! actual = getHereticUserIds todayUtc database

                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal actualIds (Set.ofList [ 21UL ; 22UL ]) "Returned heretic ids should be distinct"
                    }
                )
            ]
    ]
#endif
