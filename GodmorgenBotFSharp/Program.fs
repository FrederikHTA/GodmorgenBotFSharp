open System
open GodmorgenBotFSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord.Gateway
open NetCord.Hosting.Gateway
open NetCord.Hosting.Services.ApplicationCommands

let private parseChannelId (configuration : IConfiguration) =
    configuration.GetValue<string> "ChannelId"
    |> Option.ofObj
    |> Option.bind (fun value ->
        match UInt64.TryParse value with
        | true, parsed -> Some parsed
        | false, _ -> None
    )
    |> Option.defaultWith (fun () -> failwith "'ChannelId' is missing or not a valid uint64 in configuration.")

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

    serviceCollection.AddSingleton<IMongoDatabase> (fun sp ->
        let configuration = sp.GetRequiredService<IConfiguration> ()

        let mongoConnectionString =
            configuration.GetConnectionString "MongoDb"
            |> Option.ofObj
            |> Option.defaultWith (fun () -> failwith "'MongoDb' connection string is missing in configuration.")

        MongoDb.Functions.createDatabase mongoConnectionString
    )
    |> ignore

    serviceCollection.AddHostedService<BackgroundJob.HereticBackgroundJob> (fun sp ->
        let db = sp.GetRequiredService<IMongoDatabase> ()
        let logger = sp.GetRequiredService<ILogger<BackgroundJob.HereticBackgroundJob>> ()
        let configuration = sp.GetRequiredService<IConfiguration> ()
        let channelId = parseChannelId configuration
        let timeZone = TimeZoneInfo.FindSystemTimeZoneById "Romance Standard Time"
        let gatewayClient = sp.GetRequiredService<GatewayClient> ()
        new BackgroundJob.HereticBackgroundJob (db, logger, channelId, timeZone, gatewayClient)
    )
    |> ignore

let host =
    Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(fun _ config ->
            config.AddJsonFile ("appsettings.json", optional = false, reloadOnChange = true) |> ignore
            config.AddJsonFile ("local.settings.json", optional = true, reloadOnChange = true) |> ignore
        )
        .ConfigureServices(configureServices)
        .Build ()

let gatewayClient = host.Services.GetRequiredService<GatewayClient> ()
let db = host.Services.GetRequiredService<IMongoDatabase> ()
let loggerFactory = host.Services.GetRequiredService<ILoggerFactory> ()
let logger = loggerFactory.CreateLogger "GodmorgenBotFSharp"
let configuration = host.Services.GetRequiredService<IConfiguration> ()
let channelId = parseChannelId configuration
let timeZone = TimeZoneInfo.FindSystemTimeZoneById "Romance Standard Time"

gatewayClient.add_MessageCreate (MessageHandler.onDiscordMessage db logger timeZone)

host.AddSlashCommand (
    "leaderboard",
    "This command shows the current leaderboard status",
    SlashCommands.leaderboardCommand db logger
)
|> ignore

host.AddSlashCommand (
    "wordcount",
    "This command shows how many times the the supplied word has been used by a user.",
    SlashCommands.wordCountCommand db logger
)
|> ignore

host.AddSlashCommand (
    "giveuserpoint",
    "This command gives a user a point, if Træmand deems they deserve it.",
    SlashCommands.giveUserPointCommand db logger
)
|> ignore

host.AddSlashCommand (
    "giveuserpointwithwords",
    "This command gives a user a point, if Træmand deems they deserve it.",
    SlashCommands.giveUserPointWithWordsCommand db logger
)
|> ignore

host.AddSlashCommand (
    "topwords",
    "This command shows top 5 words for a given user",
    SlashCommands.topWordsCommand db logger
)
|> ignore

host.AddSlashCommand (
    "alltimeleaderboard",
    "This command shows the all time leaderboard, and stats for the last 3 months.",
    SlashCommands.allTimeLeaderboardCommand db logger channelId gatewayClient
)
|> ignore

host.AddSlashCommand (
    "removepointfromuser",
    "This command removes a point from a user, if Træmand deems it necessary.",
    SlashCommands.removePointCommand db logger
)
|> ignore

host.AddSlashCommand (
    "setvacation",
    "Sets a vacation period for a user so they are exempt from the heresy check. Date format: YYYY-MM-DD.",
    SlashCommands.setVacationCommand db logger
)
|> ignore

host.RunAsync () |> Async.AwaitTask |> Async.RunSynchronously
