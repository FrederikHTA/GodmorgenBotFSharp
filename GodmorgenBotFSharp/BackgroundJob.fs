module GodmorgenBotFSharp.BackgroundJob

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open NetCord.Gateway

let private calculateNextRunAtUtc (timeZone : TimeZoneInfo) (nowUtc : DateTimeOffset) : DateTimeOffset =
    let rstNow = TimeZoneInfo.ConvertTime (nowUtc, timeZone)

    let nextRunDateInRst = if rstNow.Hour < 9 then rstNow.Date else rstNow.Date.AddDays 1.0

    let nextRunInRst =
        DateTime (nextRunDateInRst.Year, nextRunDateInRst.Month, nextRunDateInRst.Day, 9, 0, 0, nextRunDateInRst.Kind)

    let nextRunUtc = TimeZoneInfo.ConvertTimeToUtc (nextRunInRst, timeZone)
    DateTimeOffset (nextRunUtc, TimeSpan.Zero)

let private findAndDisgraceHeretics (gatewayClient : GatewayClient) (context : Context) =
    task {
        context.Logger.LogInformation "Running heresy check"
        let todayUtc = DateOnly.FromDateTime DateTime.UtcNow
        let! hereticUserIds = context.MongoDataBase |> MongoDb.Functions.getHereticUserIds todayUtc

        if hereticUserIds.Length > 0 then
            let mentions =
                hereticUserIds
                |> Array.map (fun discordUserId -> $"<@%d{Domain.DiscordUserId.value discordUserId}>")
                |> String.concat ", "

            let message = $"User(s) found guilty of heresy: %s{mentions}"

            do! gatewayClient.Rest.SendMessageAsync (context.DiscordChannelInfo.ChannelId, message) :> Task
        else
            let message = "No heretics found today. All hail the righteous!"
            context.Logger.LogInformation "No heretics found."

            do! gatewayClient.Rest.SendMessageAsync (context.DiscordChannelInfo.ChannelId, message) :> Task
    }

type HereticBackgroundJob (context : Context, gatewayClient : GatewayClient) =
    inherit BackgroundService ()

    override _.ExecuteAsync (token : CancellationToken) =
        task {
            context.Logger.LogInformation "HereticBackgroundJob has been started!"

            try
                while not token.IsCancellationRequested do
                    let nowUtc = DateTimeOffset.UtcNow
                    let nextRunAtUtc = calculateNextRunAtUtc context.TimeZone nowUtc
                    let delay = nextRunAtUtc - nowUtc

                    context.Logger.LogInformation (
                        "Next heresy check scheduled for {RunAtUtc} UTC ({RunAtRst} RST)",
                        nextRunAtUtc,
                        TimeZoneInfo.ConvertTime (nextRunAtUtc, context.TimeZone)
                    )

                    do! Task.Delay (delay, token)

                    let runNowUtc = DateTimeOffset.UtcNow

                    if not (Validation.isWeekend context.TimeZone runNowUtc) then
                        try
                            do! findAndDisgraceHeretics gatewayClient context
                        with ex ->
                            context.Logger.LogError (ex, "Heresy check failed, continuing with next scheduled run")
                    else
                        context.Logger.LogInformation "Skipping heresy check - it's the weekend in RST"
            with :? OperationCanceledException when token.IsCancellationRequested ->
                context.Logger.LogInformation "HereticBackgroundJob cancellation requested."

            context.Logger.LogInformation "HereticBackgroundJob is stopping."
        }
