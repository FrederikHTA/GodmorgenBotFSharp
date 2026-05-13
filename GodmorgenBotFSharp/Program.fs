open System
open GodmorgenBotFSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open NetCord.Gateway
open NetCord.Hosting.Gateway
open NetCord.Hosting.Services.ApplicationCommands

let getConnectionStringOrThrow (environmentVariableName : string) (config : IConfiguration) =
    config.GetConnectionString environmentVariableName
    |> Option.ofObj
    |> Option.defaultWith (fun () ->
        failwith $"'{environmentVariableName}' connection string is missing in configuration."
    )

let createContextOrThrow (configuration : IConfiguration) (logger : ILogger) =
    let mongoConnectionString = configuration |> getConnectionStringOrThrow "MongoDb"

    let channelId =
        configuration.GetValue<string> "ChannelId"
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
        MongoDataBase = MongoDb.Functions.create mongoConnectionString
        Logger = logger
        DiscordChannelInfo = { ChannelId = channelId }
    }


let configureServices (_ : HostBuilderContext) (serviceCollection : IServiceCollection) =
    serviceCollection.AddDiscordGateway (fun options ->
        options.Intents <-
            GatewayIntents.GuildMessages
            ||| GatewayIntents.DirectMessages
            ||| GatewayIntents.MessageContent
            ||| GatewayIntents.DirectMessageReactions
            ||| GatewayIntents.GuildMessageReactions
    )
    |> ignore

    serviceCollection.AddApplicationCommands () |> ignore

    serviceCollection.AddHostedService<BackgroundJob.HereticBackgroundJob> (fun serviceProvider ->
        let loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory> ()
        let gatewayClient = serviceProvider.GetRequiredService<GatewayClient> ()
        let configuration = serviceProvider.GetRequiredService<IConfiguration> ()
        let logger = loggerFactory.CreateLogger<BackgroundJob.HereticBackgroundJob> ()
        let ctx = createContextOrThrow configuration logger
        new BackgroundJob.HereticBackgroundJob (ctx, gatewayClient)
    )
    |> ignore

let host =
    Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(fun _ config ->
            config.AddJsonFile ("appsettings.json", optional = false, reloadOnChange = true)
            |> ignore

            config.AddJsonFile ("local.settings.json", optional = false, reloadOnChange = true)
            |> ignore
        )
        .ConfigureServices(configureServices)
        .Build ()

let gatewayClient = host.Services.GetRequiredService<GatewayClient> ()
let loggerFactory = host.Services.GetRequiredService<ILoggerFactory> ()
let configuration = host.Services.GetRequiredService<IConfiguration> ()

let mongoConnectionString = getConnectionStringOrThrow "MongoDb" configuration
let logger = loggerFactory.CreateLogger "Program"

let ctx = createContextOrThrow configuration logger

gatewayClient.add_MessageCreate (MessageHandler.onDiscordMessage ctx)

host.AddSlashCommand (
    "leaderboard",
    "This command shows the current leaderboard status",
    SlashCommands.leaderboardCommand ctx
)
|> ignore

host.AddSlashCommand (
    "wordcount",
    "This command shows how many times the the supplied word has been used by a user.",
    SlashCommands.wordCountCommand ctx
)
|> ignore

host.AddSlashCommand (
    "giveuserpoint",
    "This command gives a user a point, if Træmand deems they deserve it.",
    SlashCommands.giveUserPointCommand ctx
)
|> ignore

host.AddSlashCommand (
    "giveuserpointwithwords",
    "This command gives a user a point, if Træmand deems they deserve it.",
    SlashCommands.giveUserPointWithWordsCommand ctx
)
|> ignore

host.AddSlashCommand (
    "topwords",
    "This command shows top 5 words for a given user",
    SlashCommands.topWordsCommand ctx
)
|> ignore

host.AddSlashCommand (
    "alltimeleaderboard",
    "This command shows the all time leaderboard, and stats for the last 3 months.",
    SlashCommands.allTimeLeaderboardCommand ctx gatewayClient
)
|> ignore

host.AddSlashCommand (
    "removepointfromuser",
    "This command removes a point from a user, if Træmand deems it necessary.",
    SlashCommands.removePointCommand ctx
)
|> ignore

host.RunAsync () |> Async.AwaitTask |> Async.RunSynchronously
