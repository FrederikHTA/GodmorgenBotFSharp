module GodmorgenBotFSharp.MessageHandler

open System
open System.Threading.Tasks
open GodmorgenBotFSharp.MongoDb.Types.GodmorgenStats
open MongoDB.Driver
open NetCord.Gateway
open FsToolkit.ErrorHandling

let messageCreate (ctx : Context) (message : Message) : ValueTask =
    task {
        let isValidGodmorgenMessage = Validation.isValidGodmorgenMessage message.Content
        let isWeekend = Validation.isWeekend DateTime.UtcNow
        let isWithinGodmorgenHours = Validation.isWithinGodmorgenHours DateTime.UtcNow

        if message.Author.IsBot || isWeekend then
            ()
        else if not isValidGodmorgenMessage || not isWithinGodmorgenHours then
            ()
        else
            let words = message.Content.Trim().ToLowerInvariant().Split ' '

            match words with
            | [| gWord ; mWord |] ->
                let userFilter =
                    Builders<MongoDb.Types.GodmorgenStats>.Filter
                        .Eq (_.DiscordUserId, message.Author.Id)

                let! godmorgenStatsO = ctx.MongoDataBase |> MongoDb.Functions.getGodmorgenStats userFilter

                match godmorgenStatsO |> Option.bind Array.tryHead with
                | None -> return ()
                | Some godmorgenStats ->
                    if godmorgenStats |> MongoDb.Types.GodmorgenStats.hasWrittenGodmorgenToday then
                        return ()
                    else
                        let! _ = ctx.MongoDataBase |> MongoDb.Functions.giveUserPoint message.Author
                        let! _ = ctx.MongoDataBase |> MongoDb.Functions.updateWordCount message.Author gWord mWord

                        if message.Author.Id = Constants.ConlonDiscordUserId then
                            message.ReplyAsync $"Godmorgen <@{message.Author.Id}>, you little bitch! :blush:" |> ignore
                        else
                            message.ReplyAsync $"Godmorgen <@{message.Author.Id}>! :sun_with_face:" |> ignore
            | _ -> ()
    }
    |> ValueTask
