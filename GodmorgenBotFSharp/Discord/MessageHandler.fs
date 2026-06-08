module GodmorgenBotFSharp.MessageHandler

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway

let private buildGreeting (authorId : uint64) =
    if authorId = Constants.ConlonDiscordUserId then
        $"Godmorgen <@{authorId}>, you little bitch! :blush:"
    else
        $"Godmorgen <@{authorId}>! :sun_with_face:"

let private processGodmorgenMessage
    (db : IMongoDatabase)
    (message : Message)
    (godmorgenMessage : Domain.GodmorgenMessage)
    =
    task {
        let! pointRecorded = MongoDb.Functions.recordDailyGodmorgen message.Author.Id DateTimeOffset.UtcNow db

        if pointRecorded then
            do! MongoDb.Functions.updateWordCount message.Author.Id godmorgenMessage.GWord godmorgenMessage.MWord db

            let! _ = message.ReplyAsync (buildGreeting message.Author.Id)
            ()
    }

let onDiscordMessage
    (db : IMongoDatabase)
    (logger : ILogger)
    (timeZone : TimeZoneInfo)
    (message : Message)
    : ValueTask =
    task {
        let utcNow = DateTimeOffset.UtcNow

        if
            message.Author.IsBot
            || Validation.isWeekend timeZone utcNow
            || not (Validation.isWithinGodmorgenHours timeZone utcNow)
        then
            return ()
        else
            logger.LogInformation (
                "Processing godmorgen message: '{Message}' from '{User}'",
                message.Content,
                message.Author.Username
            )

            match Validation.parseGodmorgenMessage message.Content with
            | Ok godmorgenMessage -> do! processGodmorgenMessage db message godmorgenMessage
            | Error validationError ->
                logger.LogError (
                    "Failed to parse godmorgen words from message: '{Message}' from '{User}', error: '{Error}'",
                    message.Content,
                    message.Author.Username,
                    validationError
                )

                ()
    }
    |> ValueTask
