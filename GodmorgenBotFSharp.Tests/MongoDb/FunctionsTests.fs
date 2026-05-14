module GodmorgenBotFSharp.Tests.MongoDb.Functions

open System
open System.Threading.Tasks
open Expecto
open MongoDB.Driver
open Testcontainers.MongoDb
open GodmorgenBotFSharp
open GodmorgenBotFSharp.MongoDb

let private sharedMongoContainer =
    lazy (
        task {
            let image : string = "mongo:7.0"
            let container = MongoDbBuilder image
            let mongoContainer = container.Build ()

            do! mongoContainer.StartAsync ()

            AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
                mongoContainer.DisposeAsync().GetAwaiter().GetResult()
            )

            return mongoContainer
        }
    )

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

let private seedGodmorgenStats
    (database : IMongoDatabase)
    (stats : Types.GodmorgenStats array)
    : Task<unit> =
    task {
        let collection =
            database.GetCollection<Types.GodmorgenStats> Functions.godmorgenStatsCollectionName

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
                        let! actual = Functions.getGodmorgenStats filter database

                        Expect.equal actual None "Expected no stats when collection has no matching documents"
                    })
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
                        let! actual = Functions.getGodmorgenStats filter database

                        match actual with
                        | None -> failtest "Expected one mapped domain record"
                        | Some stats ->
                            Expect.equal stats.Length 1 "Expected exactly one mapped domain record"

                            let mapped = stats[0]
                            Expect.equal (Domain.DiscordUserId.value mapped.UserId) dto.DiscordUserId "UserId should map"
                            Expect.equal (Domain.DiscordUsername.value mapped.Username) dto.DiscordUsername "Username should map"
                            Expect.equal (Domain.GodmorgenCount.value mapped.Count) dto.GodmorgenCount "Count should map"
                            Expect.equal (Domain.GodmorgenStreak.value mapped.Streak) dto.GodmorgenStreak "Streak should map"
                    })
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

                        let! actual = Functions.getHereticUserIds todayUtc database
                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal actualIds (Set.ofList [ 11UL ]) "Only stale users in current month should be returned"
                    })
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

                        let! actual = Functions.getHereticUserIds todayUtc database
                        let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

                        Expect.equal actualIds (Set.ofList [ 21UL ; 22UL ]) "Returned heretic ids should be distinct"
                    })
            ]
    ]
