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
