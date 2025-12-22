module GodmorgenBotFSharp.MessageHandler

open System
open System.Threading.Tasks
open MongoDB.Driver
open NetCord.Gateway
open FsToolkit.ErrorHandling

let buildFilter (date : DateTime) (authorId : uint64) : FilterDefinition<MongoDb.Types.GodmorgenStats> =
    Builders<MongoDb.Types.GodmorgenStats>.Filter
        .And (
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.Year, date.Year),
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.Month, date.Month),
            Builders<MongoDb.Types.GodmorgenStats>.Filter.Eq (_.DiscordUserId, authorId)
        )

let private shouldIgnoreMessage (message : Message) =
    let now = DateTime.UtcNow
    message.Author.IsBot
    || Validation.isWeekend now
    || not (Validation.isValidGodmorgenMessage message.Content)
    || not (Validation.isWithinGodmorgenHours now)

let private tryParseGodmorgenWords (content : string) =
    match content.Trim().ToLowerInvariant().Split ' ' with
    | [| gWord ; mWord |] when gWord.Length >= 3 && mWord.Length >= 3 -> Some (gWord, mWord)
    | _ -> None

let private buildGreeting (authorId : uint64) =
    if authorId = Constants.ConlonDiscordUserId then
        $"Godmorgen <@{authorId}>, you little bitch! :blush:"
    else
        $"Godmorgen <@{authorId}>! :sun_with_face:"

let private processGodmorgen (ctx : Context) (message : Message) (gWord : string) (mWord : string) =
    task {
        let dateNow = DateTime.UtcNow
        let filter = buildFilter dateNow message.Author.Id
        let! godmorgenStatsO = ctx.MongoDataBase |> MongoDb.Functions.getGodmorgenStats filter

        match godmorgenStatsO |> Option.bind Array.tryHead with
        | None -> ()
        | Some godmorgenStats when godmorgenStats |> MongoDb.Types.GodmorgenStats.hasWrittenGodmorgenToday -> ()
        | Some _ ->
            let! _ = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint message.Author
            let! _ = ctx.MongoDataBase |> MongoDb.Functions.updateWordCount message.Author gWord mWord
            message.ReplyAsync (buildGreeting message.Author.Id) |> ignore
    }

let onDiscordMessage (ctx : Context) (message : Message) : ValueTask =
    task {
        if shouldIgnoreMessage message then
            return ()
        else
            match tryParseGodmorgenWords message.Content with
            | Some (gWord, mWord) -> do! processGodmorgen ctx message gWord mWord
            | None -> ()
    }
    |> ValueTask
