module GodmorgenBotFSharp.BackgroundJob

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway

let rst = TimeZoneInfo.FindSystemTimeZoneById "Romance Standard Time"

let isWeekend (dt : DateTime) =
    let day = dt.DayOfWeek
    day = DayOfWeek.Saturday || day = DayOfWeek.Sunday

let isEndOfGodMorgenHoursAt (dt : DateTime) =
    let rstNow = TimeZoneInfo.ConvertTimeFromUtc (dt, rst)
    rstNow.Hour = 9 && rstNow.Minute < 10

let isWithinGodMorgenHours (dt : DateTime) =
    let rstNow = TimeZoneInfo.ConvertTimeFromUtc (dt, rst)
    rstNow.Hour >= 6 && rstNow.Hour < 9

let isValidGodMorgenMessage (message : string) =
    let parts = message.ToLowerInvariant().Split (' ', StringSplitOptions.RemoveEmptyEntries)

    match parts with
    | [| first ; second |] when first.Length > 0 && second.Length > 0 ->
        first[0] = 'g' && second[0] = 'm'
    | _ -> false

let findAndDisgraceHeretics
    (gatewayClient : GatewayClient)
    (discordChannelInfo : DiscordChannelInfo)
    (mongoDb : IMongoDatabase)
    (logger : ILogger)
    =
    async {
        logger.LogInformation "Clock is within godmorgen interval, triggering heresy check"
        let! hereticUserIds = mongoDb |> MongoDb.Types.getHereticUserIds

        if hereticUserIds.Length = 0 then
            logger.LogInformation "No heretics found."
        else
            // build a single message mentioning all heretics and send it via REST
            let mentions =
                hereticUserIds
                |> Array.map (fun discordUserId -> $"<@%d{discordUserId}>")
                |> String.concat ", "

            let message = $"User(s) found guilty of heresy: %s{mentions}"

            do!
                gatewayClient.Rest.SendMessageAsync (discordChannelInfo.ChannelId, message)
                |> Async.AwaitTask
                |> Async.Ignore
    }

let callbackFunction
    (gatewayClient : GatewayClient)
    (discordChannelInfo : DiscordChannelInfo)
    (mongoDb : IMongoDatabase)
    (logger : ILogger)
    =
    async {
        let dateTime = DateTime.UtcNow

        if dateTime |> isWeekend || dateTime |> isEndOfGodMorgenHoursAt |> not then
            logger.LogInformation
                "Not within godmorgen hours or it's weekend, skipping heresy check."
        else
            do! findAndDisgraceHeretics gatewayClient discordChannelInfo mongoDb logger
    }
    |> Async.RunSynchronously

type HereticBackgroundJob
    (
        gatewayClient : GatewayClient,
        discordChannelInfo : DiscordChannelInfo,
        mongoDb : IMongoDatabase,
        logger : ILogger<HereticBackgroundJob>
    ) =
    inherit BackgroundService ()

    let state : obj = null
    let dueTime = TimeSpan.Zero
    let period = TimeSpan.FromSeconds 5.0

    override _.ExecuteAsync (token : CancellationToken) =
        task {
            logger.LogInformation "HereticBackgroundJob has been started!"

            use _ =
                new Timer (
                    (fun _ -> callbackFunction gatewayClient discordChannelInfo mongoDb logger),
                    state,
                    dueTime,
                    period
                )

            do! Task.Delay (Timeout.Infinite, token)

            logger.LogInformation "HereticBackgroundJob is stopping."
        }
