module GodmorgenBotFSharp.MessageHandler

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway
open FsToolkit.ErrorHandling
open GodmorgenBotFSharp.Domain

let private shouldIgnoreMessage (message : Message) =
    let nowUtc = DateTimeOffset.UtcNow

    message.Author.IsBot
    || Validation.isWeekend nowUtc
    || not (Validation.isValidGodmorgenMessage message.Content)
    || not (Validation.isWithinGodmorgenHours nowUtc)

let private buildGreeting (authorId : uint64) =
    if authorId = Constants.ConlonDiscordUserId then
        $"Godmorgen <@{authorId}>, you little bitch! :blush:"
    else
        $"Godmorgen <@{authorId}>! :sun_with_face:"

let private buildAuthorFilter
    (date : DateTime)
    (authorId : uint64)
    : FilterDefinition<MongoDb.Types.GodmorgenStats> =
    Builders<MongoDb.Types.GodmorgenStats>.Filter
        .And (
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.Year, date.Year),
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.Month, date.Month),
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.DiscordUserId, authorId)
        )

let private processGodmorgenMessage
    (ctx : Context)
    (message : Message)
    (godmorgenMessage : GodmorgenMessage)
    =
    task {
        let dateNowUtc = DateTimeOffset.UtcNow
        let filter = buildAuthorFilter dateNowUtc.UtcDateTime message.Author.Id

        let! godmorgenStatsO = ctx.MongoDataBase |> MongoDb.Functions.getGodmorgenStats filter

        let hasAlreadyWrittenToday =
            godmorgenStatsO
            |> Option.bind Array.tryHead
            |> Option.map MongoDb.Types.GodmorgenStats.hasWrittenGodmorgenToday
            |> Option.defaultValue false

        if not hasAlreadyWrittenToday then
            let! _ = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint message.Author

            let! _ =
                ctx.MongoDataBase
                |> MongoDb.Functions.updateWordCount
                    message.Author
                    godmorgenMessage.GWord
                    godmorgenMessage.MWord

            do!
                message.ReplyAsync (buildGreeting message.Author.Id)
                |> Task.ignore
    }

let onDiscordMessage (ctx : Context) (message : Message) : ValueTask =
    task {
        if shouldIgnoreMessage message then
            return ()
        else
            ctx.Logger.LogInformation (
                "Processing godmorgen message: '{Message}' from '{User}'",
                message.Content,
                message.Author.Username
            )

            match Validation.parseGodmorgenMessage message.Content with
            | Ok godmorgenMessage -> do! processGodmorgenMessage ctx message godmorgenMessage
            | Error validationError ->
                ctx.Logger.LogError (
                    "Failed to parse godmorgen words from message: '{Message}' from '{User}', error: '{Error}'",
                    message.Content,
                    message.Author.Username,
                    validationError
                )

                ()
    }
    |> ValueTask
