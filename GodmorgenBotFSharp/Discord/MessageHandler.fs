module GodmorgenBotFSharp.MessageHandler

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open NetCord.Gateway

let private buildGreeting (authorId : uint64) =
    if authorId = Constants.ConlonDiscordUserId then
        $"Godmorgen <@{authorId}>, you little bitch! :blush:"
    else
        $"Godmorgen <@{authorId}>! :sun_with_face:"

let private processGodmorgenMessage (ctx : Context) (message : Message) (godmorgenMessage : Domain.GodmorgenMessage) =
    task {
        let! pointRecorded =
            MongoDb.Functions.recordDailyGodmorgen message.Author DateTimeOffset.UtcNow ctx.MongoDataBase

        if pointRecorded then
            do! MongoDb.Functions.updateWordCount message.Author godmorgenMessage.GWord godmorgenMessage.MWord ctx.MongoDataBase

            let! _ = message.ReplyAsync (buildGreeting message.Author.Id)
            ()
    }

let onDiscordMessage (ctx : Context) (message : Message) : ValueTask =
    task {
        let utcNow = DateTimeOffset.UtcNow

        if
            message.Author.IsBot
            || Validation.isWeekend ctx.TimeZone utcNow
            || not (Validation.isWithinGodmorgenHours ctx.TimeZone utcNow)
        then
            return ()

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
