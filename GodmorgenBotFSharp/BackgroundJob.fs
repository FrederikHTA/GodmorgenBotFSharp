module GodmorgenBotFSharp.BackgroundJob

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway

let private calculateNextRunAtUtc (nowUtc : DateTimeOffset) : DateTimeOffset =
    let rstNow = TimeZoneInfo.ConvertTime (nowUtc, Validation.rst)

    let nextRunDateInRst =
        if rstNow.Hour < 9 then
            rstNow.Date
        else
            rstNow.Date.AddDays 1.0

    let nextRunInRst =
        DateTime (
            nextRunDateInRst.Year,
            nextRunDateInRst.Month,
            nextRunDateInRst.Day,
            9,
            0,
            0,
            DateTimeKind.Unspecified
        )

    let nextRunUtc = TimeZoneInfo.ConvertTimeToUtc (nextRunInRst, Validation.rst)
    DateTimeOffset (nextRunUtc, TimeSpan.Zero)

let private findAndDisgraceHeretics
    (gatewayClient : GatewayClient)
    (discordChannelInfo : DiscordChannelInfo)
    (mongoDb : IMongoDatabase)
    (logger : ILogger)
    =
    async {
        logger.LogInformation "Running heresy check"
        let! hereticUserIds = mongoDb |> MongoDb.Functions.getHereticUserIds

        if hereticUserIds.Length > 0 then
            let mentions =
                hereticUserIds
                |> Array.map (fun discordUserId ->
                    $"<@%d{Domain.DiscordUserId.value discordUserId}>"
                )
                |> String.concat ", "

            let message = $"User(s) found guilty of heresy: %s{mentions}"

            do!
                gatewayClient.Rest.SendMessageAsync (discordChannelInfo.ChannelId, message)
                |> Async.AwaitTask
                |> Async.Ignore
        else
            let message = "No heretics found today. All hail the righteous!"
            logger.LogInformation "No heretics found."

            do!
                gatewayClient.Rest.SendMessageAsync (discordChannelInfo.ChannelId, message)
                |> Async.AwaitTask
                |> Async.Ignore
    }

type HereticBackgroundJob
    (
        gatewayClient : GatewayClient,
        discordChannelInfo : DiscordChannelInfo,
        mongoDb : IMongoDatabase,
        logger : ILogger<HereticBackgroundJob>
    ) =
    inherit BackgroundService ()

    override _.ExecuteAsync (token : CancellationToken) =
        task {
            logger.LogInformation "HereticBackgroundJob has been started!"

            try
                while not token.IsCancellationRequested do
                    let nowUtc = DateTimeOffset.UtcNow
                    let nextRunAtUtc = calculateNextRunAtUtc nowUtc
                    let delay = nextRunAtUtc - nowUtc

                    logger.LogInformation (
                        "Next heresy check scheduled for {RunAtUtc} UTC ({RunAtRst} RST)",
                        nextRunAtUtc,
                        TimeZoneInfo.ConvertTime (nextRunAtUtc, Validation.rst)
                    )

                    do! Task.Delay (delay, token)

                    let runNowUtc = DateTimeOffset.UtcNow

                    if not (Validation.isWeekend runNowUtc) then
                        try
                            do!
                                findAndDisgraceHeretics gatewayClient discordChannelInfo mongoDb logger
                                |> Async.StartAsTask
                        with ex ->
                            logger.LogError (ex, "Heresy check failed, continuing with next scheduled run")
                    else
                        logger.LogInformation "Skipping heresy check - it's the weekend in RST"
            with
            | :? OperationCanceledException when token.IsCancellationRequested ->
                logger.LogInformation "HereticBackgroundJob cancellation requested."

            logger.LogInformation "HereticBackgroundJob is stopping."
        }
