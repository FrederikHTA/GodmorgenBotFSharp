module GodmorgenBotFSharp.MongoDb.Functions

open System
open System.Threading
open System.Threading.Tasks
open GodmorgenBotFSharp
open GodmorgenBotFSharp.Domain
open MongoDB.Driver
open FsToolkit.ErrorHandling

[<Literal>]
let private mongoDatabaseName : string = "godmorgen"

[<Literal>]
let internal godmorgenStatsCollectionName : string = "godmorgen_stats"

[<Literal>]
let private vacationCollectionName : string = "vacations"

let createDatabase (connectionString : string) : IMongoDatabase =
    let mongoClient = new MongoClient (connectionString)
    mongoClient.GetDatabase mongoDatabaseName

let createUser (mongoDatabase : IMongoDatabase) (userId : uint64) : Task<Types.GodmorgenStats> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let newUser = Types.GodmorgenStats.create userId
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
        | Some dto -> return dto |> Mapper.toDomain |> Option.ofResult
        | None -> return None
    }

let getGodmorgenStats
    (filter : FilterDefinition<Types.GodmorgenStats>)
    (mongoDatabase : IMongoDatabase)
    : TaskResult<GodmorgenStats array option, ValidationError> =
    taskResult {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let! godmorgenStatDtos = collection.Find(filter).ToListAsync () |> Task.map Option.ofObj

        match godmorgenStatDtos with
        | Some dtos when dtos.Count > 0 ->
            let! godmorgenStats = dtos |> Seq.map Mapper.toDomain |> Seq.sequenceResultM
            return godmorgenStats |> Some
        | Some _ -> return None
        | None -> return None
    }

// Owns the id computation and the DTO write for the current month's doc -
// every caller that mutates a user's stat goes through here instead of
// re-deriving the mongoId and replace call itself.
let private replaceCurrentMonthStat
    (userId : uint64)
    (utcNow : DateTimeOffset)
    (mongoDatabase : IMongoDatabase)
    (updated : Domain.GodmorgenStats)
    : Task<Types.GodmorgenStats> =
    task {
        let collection = mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName
        let updatedDto = Mapper.fromDomain updated
        let mongoId = Types.GodmorgenStats.createMongoId userId (DateOnly.FromDateTime utcNow.UtcDateTime)

        do! collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedDto) |> Task.ignore
        return updatedDto
    }

let private toCounts
    (previous : Domain.GodmorgenStats)
    (updated : Types.GodmorgenStats)
    : Types.PreviousAndCurrentGodmorgenCount =
    {
        Previous = previous.Count |> Domain.GodmorgenCount.value
        Current = updated.GodmorgenCount
    }

let removeUserPoint (userId : uint64) (mongoDatabase : IMongoDatabase) : Task<Types.PreviousAndCurrentGodmorgenCount> =
    task {
        let! godmorgenStatO = getGodmorgenStat userId mongoDatabase

        match godmorgenStatO with
        | None -> return ({ Previous = 0 ; Current = 0 } : Types.PreviousAndCurrentGodmorgenCount)
        | Some godmorgenStat ->
            let updated = godmorgenStat |> Domain.GodmorgenStats.decreaseGodmorgenCount
            let! updatedDto = replaceCurrentMonthStat userId DateTimeOffset.UtcNow mongoDatabase updated
            return toCounts godmorgenStat updatedDto
    }

let giveUserPoint (userId : uint64) (mongoDatabase : IMongoDatabase) : Task<Types.PreviousAndCurrentGodmorgenCount> =
    task {
        let! godmorgenStatO = getGodmorgenStat userId mongoDatabase

        match godmorgenStatO with
        | None ->
            let! newUser = createUser mongoDatabase userId
            return ({ Previous = 0 ; Current = newUser.GodmorgenCount } : Types.PreviousAndCurrentGodmorgenCount)
        | Some godmorgenStat ->
            let utcNow = DateTimeOffset.UtcNow

            let updated =
                godmorgenStat
                |> Domain.GodmorgenStats.updateLastGodmorgenDate utcNow
                |> Domain.GodmorgenStats.incrementGodmorgenCount

            let! updatedDto = replaceCurrentMonthStat userId utcNow mongoDatabase updated
            return toCounts godmorgenStat updatedDto
    }

let updateWordCount
    (userId : uint64)
    (gWord : Domain.GWord)
    (mWord : Domain.MWord)
    (mongoDatabase : IMongoDatabase)
    : Task =
    task {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{userId}"
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
    (userId : uint64)
    (gWord : Domain.GWord)
    (mWord : Domain.MWord)
    (mongoDatabase : IMongoDatabase)
    : Task<Types.WordCounts> =
    task {
        let gWordVal = Domain.GWord.value gWord
        let mWordVal = Domain.MWord.value mWord
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{userId}"

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

let recordDailyGodmorgen (userId : uint64) (utcNow : DateTimeOffset) (mongoDatabase : IMongoDatabase) : Task<bool> =
    task {
        let! statO = getGodmorgenStat userId mongoDatabase

        match statO with
        | Some stat when Domain.GodmorgenStats.hasWrittenGodmorgenToday utcNow stat -> return false
        | Some stat ->
            let updated =
                stat
                |> Domain.GodmorgenStats.updateLastGodmorgenDate utcNow
                |> Domain.GodmorgenStats.incrementGodmorgenCount

            do! replaceCurrentMonthStat userId utcNow mongoDatabase updated |> Task.ignore
            return true
        | None ->
            do! createUser mongoDatabase userId |> Task.ignore
            return true
    }

let getStatsByMonth
    (month : int)
    (year : int)
    (mongoDatabase : IMongoDatabase)
    : TaskResult<Domain.GodmorgenStats array option, Domain.ValidationError> =
    let filter =
        Builders<Types.GodmorgenStats>.Filter
            .And (
                Builders<Types.GodmorgenStats>.Filter.Eq (_.Year, year),
                Builders<Types.GodmorgenStats>.Filter.Eq (_.Month, month)
            )

    getGodmorgenStats filter mongoDatabase

let getAllStats
    (mongoDatabase : IMongoDatabase)
    : TaskResult<Domain.GodmorgenStats array option, Domain.ValidationError> =
    getGodmorgenStats Builders<Types.GodmorgenStats>.Filter.Empty mongoDatabase

let getTop5Words (userId : uint64) (mongoDatabase : IMongoDatabase) : Task<Types.WordCount array option> =
    task {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{userId}"

        let! results =
            collection.Find(fun _ -> true).SortByDescending(_.Count).Limit(5).ToListAsync ()
            |> Task.map Option.ofObj

        return results |> Option.map Seq.toArray
    }

let upsertVacation
    (userId : uint64)
    (startDate : DateOnly)
    (endDate : DateOnly)
    (mongoDatabase : IMongoDatabase)
    : Task =
    task {
        let collection = mongoDatabase.GetCollection<Types.Vacation> vacationCollectionName

        let vacation : Types.Vacation = {
            Id = string userId
            DiscordUserId = userId
            StartDate = startDate.ToDateTime (TimeOnly.MinValue, DateTimeKind.Utc) |> DateTimeOffset
            EndDate = endDate.ToDateTime (TimeOnly.MaxValue, DateTimeKind.Utc) |> DateTimeOffset
        }

        let options = ReplaceOptions (IsUpsert = true)
        do! collection.ReplaceOneAsync ((fun x -> x.Id = string userId), vacation, options) |> Task.ignore
    }

let getVacationingUserIds (utcToday : DateOnly) (mongoDatabase : IMongoDatabase) : Task<uint64 Set> =
    task {
        let collection = mongoDatabase.GetCollection<Types.Vacation> vacationCollectionName
        let todayStart = utcToday.ToDateTime (TimeOnly.MinValue, DateTimeKind.Utc) |> DateTimeOffset

        let filter =
            Builders<Types.Vacation>.Filter
                .And (
                    Builders<Types.Vacation>.Filter.Lte (_.StartDate, todayStart),
                    Builders<Types.Vacation>.Filter.Gte (_.EndDate, todayStart)
                )

        let! results = collection.Find(filter).ToListAsync () |> Task.map Option.ofObj
        return results |> Option.map (Seq.map _.DiscordUserId >> Set.ofSeq) |> Option.defaultValue Set.empty
    }
