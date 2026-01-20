module GodmorgenBotFSharp.BackgroundJob

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway

let private calculateDelayUntilNextRun () =
    let utcNow = DateTime.UtcNow
    let rstNow = TimeZoneInfo.ConvertTimeFromUtc (utcNow, Validation.rst)

    // Always schedule for 9:00 AM tomorrow
    let tomorrow = rstNow.Date.AddDays 1.0
    let targetTime = DateTime (tomorrow.Year, tomorrow.Month, tomorrow.Day, 9, 0, 0)
    let targetTimeUtc = TimeZoneInfo.ConvertTimeToUtc (targetTime, Validation.rst)

    targetTimeUtc - utcNow

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

            while not token.IsCancellationRequested do
                let delay = calculateDelayUntilNextRun ()
                logger.LogInformation ("Next heresy check scheduled in {Delay}", delay)

                do! Task.Delay (delay, token)

                let dateTime = DateTime.UtcNow

                if not (Validation.isWeekend dateTime) then
                    do!
                        findAndDisgraceHeretics gatewayClient discordChannelInfo mongoDb logger
                        |> Async.StartAsTask
                else
                    logger.LogInformation "Skipping heresy check - it's the weekend"

            logger.LogInformation "HereticBackgroundJob is stopping."
        }
