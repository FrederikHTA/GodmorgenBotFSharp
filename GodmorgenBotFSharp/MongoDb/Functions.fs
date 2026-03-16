module GodmorgenBotFSharp.MongoDb.Functions

open System
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

let create (connectionString : string) : IMongoDatabase =
    let mongoClient = new MongoClient (connectionString)
    mongoClient.GetDatabase mongoDatabaseName

let mapToDomain (dto : Types.GodmorgenStats) : Domain.GodmorgenStats =
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

let getGodmorgenStats
    (filter : FilterDefinition<Types.GodmorgenStats>)
    (mongoDatabase : IMongoDatabase)
    : Async<Option<Array<Domain.GodmorgenStats>>> =
    async {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! results =
            collection.Find(filter).ToListAsync () |> Async.AwaitTask |> Async.map Option.ofObj

        match results with
        | Some dtos when dtos.Count > 0 ->
            let domainStats = dtos |> Seq.map mapToDomain |> Seq.toList
            return List.toArray domainStats |> Some
        | Some _ -> return None
        | None -> return None
    }

let removeUserPoint
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Task<PreviousAndCurrentGodmorgenCount> =
    task {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId =
            Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

        let! mongoUserO =
            collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync ()
            |> Task.map Option.ofObj

        match mongoUserO with
        | None ->
            return {
                Previous = 0
                Current = 0
            }
        | Some value ->
            let updatedUser = {
                value with
                    GodmorgenCount = Math.Max (0, value.GodmorgenCount - 1)
                    GodmorgenStreak = Math.Max (0, value.GodmorgenStreak - 1)
            }

            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser) |> Task.ignore

            return {
                Previous = value.GodmorgenCount
                Current = updatedUser.GodmorgenCount
            }
    }

let giveUserPoint
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Task<PreviousAndCurrentGodmorgenCount> =
    task {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId =
            Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

        let! mongoUserO =
            collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync ()
            |> Task.map Option.ofObj

        match mongoUserO with
        | None ->
            let newUser = Types.GodmorgenStats.create user.Id user.Username

            do! collection.InsertOneAsync newUser

            return {
                Previous = 0
                Current = 1
            }
        | Some value ->
            let updatedUser = Types.GodmorgenStats.increaseGodmorgenCount value

            let! _ =
                collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser)
                |> Task.map Option.ofObj

            return {
                Previous = value.GodmorgenCount
                Current = updatedUser.GodmorgenCount
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

        let upsertWord (word : string) =
            let trimmedWord = word.Trim().ToLowerInvariant ()
            let filter = Builders<Types.WordCount>.Filter.Eq (_.Word, trimmedWord)
            let update = Builders<Types.WordCount>.Update.Inc (_.Count, 1)
            let options = FindOneAndUpdateOptions<Types.WordCount> ()
            options.IsUpsert <- true

            collection.FindOneAndUpdateAsync (filter, update, options)

        do!
            [ upsertWord (Domain.GWord.value gWord) ; upsertWord (Domain.MWord.value mWord) ]
            |> Task.WhenAll
            |> Task.ignore

        return ()
    }

let getHereticUserIds (mongoDatabase : IMongoDatabase) : Async<Array<Domain.DiscordUserId>> =
    async {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
        let currentMonth = todayUtc.Month
        let currentYear = todayUtc.Year

        let! godmorgenStatsO =
            collection
                .Find(fun msg -> msg.Month = currentMonth && msg.Year = currentYear)
                .ToListAsync ()
            |> Async.AwaitTask
            |> Async.map Option.ofObj

        return
            godmorgenStatsO
            |> Option.map (fun messages ->
                messages
                |> Seq.filter (fun x -> DateOnly.FromDateTime x.LastGoodmorgenDate.UtcDateTime < todayUtc)
                |> Seq.map (fun x -> x.DiscordUserId |> Domain.DiscordUserId.create)
                |> Seq.distinct
                |> Array.ofSeq
            )
            |> Option.defaultValue Array.empty
    }

let getWordCount
    (user : NetCord.User)
    (gWord : Domain.GWord)
    (mWord : Domain.MWord)
    (mongoDatabase : IMongoDatabase)
    : Async<
          {|
              GWord : Types.WordCount
              MWord : Types.WordCount
          |}
       >
    =
    async {
        let gWordVal = Domain.GWord.value gWord
        let mWordVal = Domain.MWord.value mWord
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let filter = Builders<Types.WordCount>.Filter.In (_.Word, [| gWordVal ; mWordVal |])

        let! wordCounts =
            collection.Find(filter).ToListAsync () |> Async.AwaitTask |> Async.map Option.ofObj

        let counts =
            wordCounts
            |> Option.map Seq.toArray
            |> Option.defaultValue Array.empty
            |> Array.map (fun x -> x.Word.ToLowerInvariant (), x.Count)
            |> Map.ofArray

        let findOrDefault (word : string) (wordLower : string) =
            counts
            |> Map.tryFind wordLower
            |> Option.map (fun count -> Types.WordCount.create word count)
            |> Option.defaultValue (Types.WordCount.empty wordLower)

        return {|
            GWord = findOrDefault gWordVal gWordVal
            MWord = findOrDefault mWordVal mWordVal
        |}
    }

let getTop5Words
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Async<Option<Array<Types.WordCount>>> =
    async {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let! results =
            collection.Find(fun _ -> true).SortByDescending(_.Count).Limit(5).ToListAsync ()
            |> Async.AwaitTask
            |> Async.map Option.ofObj

        return results |> Option.map Seq.toArray
    }


#if DEBUG
open Expecto
open Testcontainers.MongoDb

let private withMongoDatabase (runTest : IMongoDatabase -> Task<unit>) : Task<unit> =
    task {
        let image : string = "mongo:7.0"
        let container = MongoDbBuilder image
        let mongoContainer = container.Build ()

        try
            do! mongoContainer.StartAsync ()
            let database = create (mongoContainer.GetConnectionString ())
            do! runTest database
        finally
            mongoContainer.DisposeAsync().GetAwaiter().GetResult()
    }

let private seedGodmorgenStats
    (database : IMongoDatabase)
    (stats : Array<Types.GodmorgenStats>)
    : Task<unit> =
    task {
        let collection = database.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName
        do! collection.InsertManyAsync stats
    }

[<Tests>]
let tests =
    testList "MongoDb.Functions tests" [
        testTask "getGodmorgenStats returns None when there are no matches" {
            do!
                withMongoDatabase (fun database ->
                    task {
                        let filter = Builders<Types.GodmorgenStats>.Filter.Eq (_.DiscordUserId, 123UL)
                        let! actual = getGodmorgenStats filter database

                        Expect.equal actual None "Expected no stats when collection has no matching documents"
                    }
                )
        }

        testTask "getGodmorgenStats maps a stored document to domain" {
            do!
                withMongoDatabase (fun database ->
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
                            Expect.equal (Domain.DiscordUserId.value mapped.UserId) dto.DiscordUserId "UserId should map"
                            Expect.equal (Domain.DiscordUsername.value mapped.Username) dto.DiscordUsername "Username should map"
                            Expect.equal (Domain.GodmorgenCount.value mapped.Count) dto.GodmorgenCount "Count should map"
                            Expect.equal (Domain.GodmorgenStreak.value mapped.Streak) dto.GodmorgenStreak "Streak should map"
                    }
                )
        }

        testTask "getHereticUserIds returns users who have not written today in current month" {
            do!
                withMongoDatabase (fun database ->
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
                            Year =
                                if todayUtc.Month = 1 then
                                    todayUtc.Year - 1
                                else
                                    todayUtc.Year
                            Month =
                                if todayUtc.Month = 1 then
                                    12
                                else
                                    todayUtc.Month - 1
                        }

                        do! seedGodmorgenStats database [| staleInCurrentMonth ; currentDayUser ; staleOtherMonth |]

                        let! actual = getHereticUserIds database
                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal actualIds (Set.ofList [ 11UL ]) "Only stale users in current month should be returned"
                    }
                )
        }

        testTask "getHereticUserIds returns distinct user ids" {
            do!
                withMongoDatabase (fun database ->
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

                        let! actual = getHereticUserIds database
                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal actualIds (Set.ofList [ 21UL ; 22UL ]) "Returned heretic ids should be distinct"
                    }
                )
        }
    ]
#endif
