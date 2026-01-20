module GodmorgenBotFSharp.MongoDb.Functions

open System
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
    : Async<PreviousAndCurrentGodmorgenCount> =
    async {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId = Types.GodmorgenStats.createMongoId user.Id DateTime.Today

        let! mongoUserO =
            collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync ()
            |> Async.AwaitTask
            |> Async.map Option.ofObj

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

            let! _ =
                collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser)
                |> Async.AwaitTask
                |> Async.map Option.ofObj

            return {
                Previous = value.GodmorgenCount
                Current = updatedUser.GodmorgenCount
            }
    }

let giveUserPoint
    (user : NetCord.User)
    (mongoDatabase : IMongoDatabase)
    : Async<PreviousAndCurrentGodmorgenCount> =
    async {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let mongoId = Types.GodmorgenStats.createMongoId user.Id DateTime.Today

        let! mongoUserO =
            collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync ()
            |> Async.AwaitTask
            |> Async.map Option.ofObj

        match mongoUserO with
        | None ->
            let newUser = Types.GodmorgenStats.create user.Id user.Username

            do! collection.InsertOneAsync newUser |> Async.AwaitTask

            return {
                Previous = 0
                Current = 1
            }
        | Some value ->
            let updatedUser = Types.GodmorgenStats.increaseGodmorgenCount value

            let! _ =
                collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser)
                |> Async.AwaitTask
                |> Async.map Option.ofObj

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
    : Async<unit> =
    async {
        let collection = mongoDatabase.GetCollection<Types.WordCount> $"word_count_{user.Id}"

        let upsertWord (word : string) : Async<Option<Types.WordCount>> =
            let trimmedWord = word.Trim().ToLowerInvariant ()
            let filter = Builders<Types.WordCount>.Filter.Eq (_.Word, trimmedWord)
            let update = Builders<Types.WordCount>.Update.Inc (_.Count, 1)
            let options = FindOneAndUpdateOptions<Types.WordCount> ()
            options.IsUpsert <- true

            collection.FindOneAndUpdateAsync (filter, update, options)
            |> Async.AwaitTask
            |> Async.map Option.ofObj

        let! _ =
            [ upsertWord (Domain.GWord.value gWord) ; upsertWord (Domain.MWord.value mWord) ]
            |> Async.Parallel

        return ()
    }

let getHereticUserIds (mongoDatabase : IMongoDatabase) : Async<Array<Domain.DiscordUserId>> =
    async {
        let collection =
            mongoDatabase.GetCollection<Types.GodmorgenStats> godmorgenStatsCollectionName

        let today = DateTime.UtcNow.Date
        let currentMonth = today.Month
        let currentYear = today.Year

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
                |> Seq.filter (fun x -> x.LastGoodmorgenDate.Date < today)
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
