module GodmorgenBotFSharp.MongoDb.Functions

open System
open System.Threading.Tasks
open GodmorgenBotFSharp
open GodmorgenBotFSharp.MongoDb.Types
open MongoDB.Driver
open FsToolkit.ErrorHandling

[<Literal>]
let mongoDatabaseName : string = "godmorgen"

[<Literal>]
let godmorgenStatsCollectionName : string = "godmorgen_stats"

let create (connectionString : string) : IMongoDatabase =
    let mongoClient = new MongoClient (connectionString)
    let database = mongoClient.GetDatabase mongoDatabaseName
    database

let getGodmorgenStats (filter : FilterDefinition<GodmorgenStats>) (mongoDatabase : IMongoDatabase) =
    task {
        let collection = mongoDatabase.GetCollection<GodmorgenStats> godmorgenStatsCollectionName
        let! results = collection.Find(filter).ToListAsync () |> Task.map Option.ofNull
        return results |> Option.map Seq.toArray
    }

let removeUserPoint (user : NetCord.User) (mongoDatabase : IMongoDatabase) =
    task {
        let collection = mongoDatabase.GetCollection<GodmorgenStats> godmorgenStatsCollectionName
        let mongoId = GodmorgenStats.createMongoId user.Id DateTime.Today
        let! mongoUserO = collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync () |> Task.map Option.ofNull

        match mongoUserO with
        | None ->
            return {|
                Previous = 0
                Current = 0
            |}
        | Some value ->
            let updatedUser = {
                value with
                    GodmorgenCount = Math.Max (0, value.GodmorgenCount - 1)
                    GodmorgenStreak = Math.Max (0, value.GodmorgenStreak - 1)
            }

            let! _ = collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser)

            return {|
                Previous = value.GodmorgenCount
                Current = updatedUser.GodmorgenCount
            |}
    }

let giveUserPoint (user : NetCord.User) (mongoDatabase : IMongoDatabase) =
    task {
        let collection = mongoDatabase.GetCollection<GodmorgenStats> godmorgenStatsCollectionName
        let mongoId = GodmorgenStats.createMongoId user.Id DateTime.Today
        let! mongoUserO = collection.Find(fun x -> x.Id = mongoId).FirstOrDefaultAsync () |> Task.map Option.ofNull

        match mongoUserO with
        | None ->
            let newUser = GodmorgenStats.create user.Id user.Username
            do! collection.InsertOneAsync newUser

            return {|
                Previous = 0
                Current = 1
            |}
        | Some value ->
            let updatedUser = GodmorgenStats.increaseGodmorgenCount value
            let! _ = collection.ReplaceOneAsync ((fun x -> x.Id = mongoId), updatedUser)

            return {|
                Previous = value.GodmorgenCount
                Current = updatedUser.GodmorgenCount
            |}
    }

let updateWordCount (user : NetCord.User) (gWord : string) (mWord : string) (mongoDatabase : IMongoDatabase) =
    taskResult {
        let gWordLower = gWord.ToLowerInvariant ()
        let mWordLower = mWord.ToLowerInvariant ()

        do! Validation.validateWord gWordLower 'g'
        do! Validation.validateWord mWordLower 'm'

        let collection = mongoDatabase.GetCollection<WordCount> $"word_count_{user.Id}"

        let upsertWord (word : string) =
            let trimmedWord = word.Trim().ToLowerInvariant ()
            let filter = Builders<WordCount>.Filter.Eq (_.Word, trimmedWord)
            let update = Builders<WordCount>.Update.Inc (_.Count, 1)
            let options = FindOneAndUpdateOptions<WordCount> ()
            options.IsUpsert <- true
            collection.FindOneAndUpdateAsync (filter, update, options)

        let! _ = Task.WhenAll [| upsertWord gWord ; upsertWord mWord |]
        return ()
    }

let getHereticUserIds (mongoDatabase : IMongoDatabase) =
    task {
        let collection = mongoDatabase.GetCollection<GodmorgenStats> godmorgenStatsCollectionName
        let today = DateTime.UtcNow.Date
        let currentMonth = today.Month
        let currentYear = today.Year

        let! godmorgenStatsO =
            collection.Find(fun msg -> msg.Month = currentMonth && msg.Year = currentYear).ToListAsync ()
            |> Task.map Option.ofNull

        return
            godmorgenStatsO
            |> Option.map (fun messages ->
                messages
                |> Seq.filter (fun x -> x.LastGoodmorgenDate.Date < today)
                |> Seq.map _.DiscordUserId
                |> Seq.distinct
                |> Array.ofSeq
            )
            |> Option.defaultValue Array.empty
    }

let getWordCount (user : NetCord.User) (gWord : string) (mWord : string) (mongoDatabase : IMongoDatabase) =
    taskResult {
        let gWordLower = gWord.ToLowerInvariant ()
        let mWordLower = mWord.ToLowerInvariant ()

        do! Validation.validateWord gWordLower 'g'
        do! Validation.validateWord mWordLower 'm'

        let collection = mongoDatabase.GetCollection<WordCount> $"word_count_{user.Id}"
        let filter = Builders<WordCount>.Filter.In (_.Word, [| gWordLower ; mWordLower |])

        let! wordCounts = collection.Find(filter).ToListAsync () |> Task.map Option.ofNull

        let counts =
            wordCounts
            |> Option.map Seq.toArray
            |> Option.defaultValue Array.empty
            |> Array.map (fun x -> x.Word.ToLowerInvariant (), x.Count)
            |> Map.ofArray

        let findOrDefault (word : string) (wordLower : string) =
            counts
            |> Map.tryFind wordLower
            |> Option.map (fun count -> { Word = word ; Count = count })
            |> Option.defaultValue (WordCount.empty wordLower)

        return {|
            GWord = findOrDefault gWord gWordLower
            MWord = findOrDefault mWord mWordLower
        |}
    }

let getTop5Words (user : NetCord.User) (mongoDatabase : IMongoDatabase) =
    task {
        let collection = mongoDatabase.GetCollection<WordCount> $"word_count_{user.Id}"

        let! results =
            collection.Find(fun _ -> true).SortByDescending(_.Count).Limit(5).ToListAsync () |> Task.map Option.ofNull

        return results |> Option.map Seq.toArray
    }
