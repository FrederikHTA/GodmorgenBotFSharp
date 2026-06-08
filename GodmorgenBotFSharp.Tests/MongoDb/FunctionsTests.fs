module GodmorgenBotFSharp.Tests.MongoDb.FunctionsIntegration

open System
open System.Threading.Tasks
open Xunit
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

let private seedGodmorgenStats (database : IMongoDatabase) (stats : Types.GodmorgenStats array) : Task<unit> =
    task {
        let collection = database.GetCollection<Types.GodmorgenStats> Functions.godmorgenStatsCollectionName
        do! collection.InsertManyAsync stats
    }

[<Fact>]
let ``getHereticUserIds - filters stale users in current month`` () : Task =
    withMongoDatabase (fun database ->
        task {
            let nowUtc = DateTimeOffset.UtcNow
            let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
            let oldDate = nowUtc.AddDays -1.0

            let staleInCurrentMonth : Types.GodmorgenStats = {
                Id = "11_stale"
                DiscordUserId = 11UL

                LastGoodmorgenDate = oldDate
                GodmorgenCount = 2
                GodmorgenStreak = 1
                Year = todayUtc.Year
                Month = todayUtc.Month
            }

            let currentDayUser : Types.GodmorgenStats = {
                Id = "12_today"
                DiscordUserId = 12UL

                LastGoodmorgenDate = nowUtc
                GodmorgenCount = 7
                GodmorgenStreak = 2
                Year = todayUtc.Year
                Month = todayUtc.Month
            }

            let staleOtherMonth : Types.GodmorgenStats = {
                Id = "13_other_month"
                DiscordUserId = 13UL

                LastGoodmorgenDate = oldDate
                GodmorgenCount = 4
                GodmorgenStreak = 1
                Year = if todayUtc.Month = 1 then todayUtc.Year - 1 else todayUtc.Year
                Month = if todayUtc.Month = 1 then 12 else todayUtc.Month - 1
            }

            do! seedGodmorgenStats database [| staleInCurrentMonth ; currentDayUser ; staleOtherMonth |]

            let! actual = Functions.getHereticUserIds todayUtc database
            let actualIds = actual |> Array.map Domain.DiscordUserId.value |> Set.ofArray

            Expect.equal actualIds (Set.ofList [ 11UL ]) "Only stale users in current month should be returned"
        }
    )

[<Fact>]
let ``getHereticUserIds - returns distinct user ids`` () : Task =
    withMongoDatabase (fun database ->
        task {
            let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
            let oldDate = DateTimeOffset.UtcNow.AddDays -2.0

            let duplicatedUserA : Types.GodmorgenStats = {
                Id = "21_dup_a"
                DiscordUserId = 21UL

                LastGoodmorgenDate = oldDate
                GodmorgenCount = 1
                GodmorgenStreak = 1
                Year = todayUtc.Year
                Month = todayUtc.Month
            }

            let duplicatedUserB : Types.GodmorgenStats = {
                Id = "21_dup_b"
                DiscordUserId = 21UL

                LastGoodmorgenDate = oldDate
                GodmorgenCount = 2
                GodmorgenStreak = 2
                Year = todayUtc.Year
                Month = todayUtc.Month
            }

            let uniqueUser : Types.GodmorgenStats = {
                Id = "22_unique"
                DiscordUserId = 22UL

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
        }
    )
