module GodmorgenBotFSharp.BackgroundJob

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway

let private calculateNextRunAtUtc (timeZone : TimeZoneInfo) (nowUtc : DateTimeOffset) : DateTimeOffset =
    let rstNow = TimeZoneInfo.ConvertTime (nowUtc, timeZone)

    let nextRunDateInRst = if rstNow.Hour < 9 then rstNow.Date else rstNow.Date.AddDays 1.0

    let nextRunInRst =
        DateTime (nextRunDateInRst.Year, nextRunDateInRst.Month, nextRunDateInRst.Day, 9, 0, 0, nextRunDateInRst.Kind)

    let nextRunUtc = TimeZoneInfo.ConvertTimeToUtc (nextRunInRst, timeZone)
    DateTimeOffset (nextRunUtc, TimeSpan.Zero)

let private findAndDisgraceHeretics (db : IMongoDatabase) (logger : ILogger) (channelId : uint64) (gatewayClient : GatewayClient) =
    task {
        logger.LogInformation "Running heresy check"
        let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
        let! hereticUserIds = db |> MongoDb.Functions.getHereticUserIds todayUtc

        if hereticUserIds.Length > 0 then
            let mentions =
                hereticUserIds
                |> Array.map (fun discordUserId -> $"<@%d{Domain.DiscordUserId.value discordUserId}>")
                |> String.concat ", "

            let message = $"User(s) found guilty of heresy: %s{mentions}"

            do! gatewayClient.Rest.SendMessageAsync (channelId, message) :> Task
        else
            let message = "No heretics found today. All hail the righteous!"
            logger.LogInformation "No heretics found."

            do! gatewayClient.Rest.SendMessageAsync (channelId, message) :> Task
    }

type HereticBackgroundJob (db : IMongoDatabase, logger : ILogger<HereticBackgroundJob>, channelId : uint64, timeZone : TimeZoneInfo, gatewayClient : GatewayClient) =
    inherit BackgroundService ()

    override _.ExecuteAsync (token : CancellationToken) =
        task {
            logger.LogInformation "HereticBackgroundJob has been started!"

            try
                while not token.IsCancellationRequested do
                    let nowUtc = DateTimeOffset.UtcNow
                    let nextRunAtUtc = calculateNextRunAtUtc timeZone nowUtc
                    let delay = nextRunAtUtc - nowUtc

                    logger.LogInformation (
                        "Next heresy check scheduled for {RunAtUtc} UTC ({RunAtRst} RST)",
                        nextRunAtUtc,
                        TimeZoneInfo.ConvertTime (nextRunAtUtc, timeZone)
                    )

                    do! Task.Delay (delay, token)

                    let runNowUtc = DateTimeOffset.UtcNow

                    if not (Validation.isWeekend timeZone runNowUtc) then
                        try
                            do! findAndDisgraceHeretics db logger channelId gatewayClient
                        with ex ->
                            logger.LogError (ex, "Heresy check failed, continuing with next scheduled run")
                    else
                        logger.LogInformation "Skipping heresy check - it's the weekend in RST"
            with :? OperationCanceledException when token.IsCancellationRequested ->
                logger.LogInformation "HereticBackgroundJob cancellation requested."

            logger.LogInformation "HereticBackgroundJob is stopping."
        }
