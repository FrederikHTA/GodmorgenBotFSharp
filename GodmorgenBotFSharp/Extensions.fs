namespace GodmorgenBotFSharp

open System
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

[<AutoOpen>]
module ConfigurationExtensions =
    type IConfiguration with
        member this.getConnectionStringOrThrow (environmentVariableName : string) : string =
            this.GetConnectionString environmentVariableName
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                failwith $"'{environmentVariableName}' connection string is missing in configuration."
            )

        member this.createContextOrThrow (logger : ILogger) : Context =
            let mongoConnectionString = this.getConnectionStringOrThrow "MongoDb"

            let channelId =
                this.GetValue<string> "ChannelId"
                |> Option.ofObj
                |> Option.bind (fun value ->
                    match UInt64.TryParse value with
                    | true, parsedValue -> Some parsedValue
                    | false, _ -> None
                )
                |> Option.defaultWith (fun () ->
                    failwith
                        "Invalid 'ChannelId' value in configuration. Must be a valid unsigned 64-bit integer, saved as a string in the configuration."
                )

            {
                MongoDataBase = MongoDb.Functions.createDatabase mongoConnectionString
                Logger = logger
                DiscordChannelInfo = { ChannelId = channelId }
            }
