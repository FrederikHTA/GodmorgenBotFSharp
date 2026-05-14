module GodmorgenBotFSharp.MongoDb.Functions

open System
open System.Threading
open System.Threading.Tasks
open GodmorgenBotFSharp
open MongoDB.Driver
open FsToolkit.ErrorHandling

[<Literal>]
let private mongoDatabaseName : string = "godmorgen"

[<Literal>]
let internal godmorgenStatsCollectionName : string = "godmorgen_stats"

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

let getGodmorgenStat (userId : uint64) (mongoDatabase : IMongoDatabase) : Task<Domain.GodmorgenStats option> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId = Types.GodmorgenStats.createMongoId userId (DateOnly.FromDateTime DateTime.UtcNow)

        let! godmorgenStatDto =
            collection.Find(fun x -> x.Id = mongoId).SingleOrDefaultAsync () |> Task.map Option.ofObj

        match godmorgenStatDto with
        | Some dto -> return dto |> Mapper.toDomain |> Some
        | None -> return None
    }

let getGodmorgenStats
    (filter : FilterDefinition<Types.GodmorgenStats>)
    (mongoDatabase : IMongoDatabase)
    : Task<Domain.GodmorgenStats array option> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatDtos = collection.Find(filter).ToListAsync () |> Task.map Option.ofObj

        match godmorgenStatDtos with
        | Some dtos when dtos.Count > 0 -> return dtos |> Seq.map Mapper.toDomain |> Seq.toArray |> Some
        | Some _ -> return None
        | None -> return None
    }

let removeUserPoint
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Task<Types.PreviousAndCurrentGodmorgenCount> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatO = getGodmorgenStat user.Id mongoDatabase

        match godmorgenStatO with
        | None ->
            let godmorgenCounts : Types.PreviousAndCurrentGodmorgenCount = {
                Previous = 0
                Current = 0
            }

            return godmorgenCounts
        | Some godmorgenStat ->
            let updatedGodmorgenStats =
                godmorgenStat |> Domain.GodmorgenStats.decreaseGodmorgenCount |> Mapper.fromDomain

            let mongoId = Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedGodmorgenStats) |> Task.ignore

            let godmorgenCounts : Types.PreviousAndCurrentGodmorgenCount = {
                Previous = godmorgenStat.Count |> Domain.GodmorgenCount.value
                Current = updatedGodmorgenStats.GodmorgenCount
            }

            return godmorgenCounts
    }

let giveUserPoint
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Task<Types.PreviousAndCurrentGodmorgenCount> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatO = getGodmorgenStat user.Id mongoDatabase

        match godmorgenStatO with
        | None ->
            let! newUser = createUser mongoDatabase user.Id user.Username

            let godmorgenCounts : Types.PreviousAndCurrentGodmorgenCount = {
                Previous = 0
                Current = newUser.GodmorgenCount
            }

            return godmorgenCounts
        | Some godmorgenStats ->
            let updatedGodmorgenStats =
                godmorgenStats
                |> Domain.GodmorgenStats.updateLastGodmorgenDate DateTimeOffset.UtcNow
                |> Domain.GodmorgenStats.incrementGodmorgenCount
                |> Mapper.fromDomain

            let mongoId = Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime DateTime.UtcNow)

            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedGodmorgenStats) |> Task.ignore

            let godmorgenCounts : Types.PreviousAndCurrentGodmorgenCount = {
                Previous = godmorgenStats.Count |> Domain.GodmorgenCount.value
                Current = updatedGodmorgenStats.GodmorgenCount
            }

            return godmorgenCounts
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

let getHereticUserIds (utcNow : DateOnly) (mongoDatabase : IMongoDatabase) : Task<Domain.DiscordUserId array> =
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
    : Task<Types.WordCounts> =
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

        let wordCounts : Types.WordCounts = {
            GWord = findOrDefault gWordVal
            MWord = findOrDefault mWordVal
        }

        return wordCounts
    }

let recordDailyGodmorgen (user : NetCord.User) (utcNow : DateTimeOffset) (mongoDatabase : IMongoDatabase) : Task<bool> =
    task {
        let! statO = getGodmorgenStat user.Id mongoDatabase

        match statO with
        | Some stat when Domain.GodmorgenStats.hasWrittenGodmorgenToday utcNow stat -> return false
        | Some stat ->
            let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

            let updated =
                stat
                |> Domain.GodmorgenStats.updateLastGodmorgenDate utcNow
                |> Domain.GodmorgenStats.incrementGodmorgenCount
                |> Mapper.fromDomain

            let mongoId = Types.GodmorgenStats.createMongoId user.Id (DateOnly.FromDateTime utcNow.UtcDateTime)
            do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updated) |> Task.ignore
            return true
        | None ->
            do! createUser mongoDatabase user.Id user.Username |> Task.ignore
            return true
    }

let getStatsByMonth
    (month : int)
    (year : int)
    (mongoDatabase : IMongoDatabase)
    : Task<Domain.GodmorgenStats array option> =
    let filter =
        Builders<Types.GodmorgenStats>.Filter
            .And (
                Builders<Types.GodmorgenStats>.Filter.Eq (_.Year, year),
                Builders<Types.GodmorgenStats>.Filter.Eq (_.Month, month)
            )

    getGodmorgenStats filter mongoDatabase

let getAllStats (mongoDatabase : IMongoDatabase) : Task<Domain.GodmorgenStats array option> =
    getGodmorgenStats Builders<Types.GodmorgenStats>.Filter.Empty mongoDatabase

let getTop5Words (user : NetCord.User) (mongoDatabase : IMongoDatabase) : Task<Types.WordCount array option> =
    task {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let! results =
            collection.Find(fun _ -> true).SortByDescending(_.Count).Limit(5).ToListAsync ()
            |> Task.map Option.ofObj

        return results |> Option.map Seq.toArray
    }
