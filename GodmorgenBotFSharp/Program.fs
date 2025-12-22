open System.Threading.Tasks
open GodmorgenBotFSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open NetCord.Gateway
open NetCord.Hosting.Gateway
open NetCord.Hosting.Services.ApplicationCommands

let createContext (config : IConfiguration) (loggerFactory : ILoggerFactory) =
    let mongoConnectionString = config.GetConnectionString "MongoDb"

    {
        MongoDataBase = MongoDb.Functions.create mongoConnectionString
        Logger = loggerFactory.CreateLogger "GodmorgenBot"
        DiscordChannelInfo = {
            ChannelId = config.GetValue<uint64> "ChannelId"
            GuildId = config.GetValue<uint64> "GuildId"
        }
    }

let configureServices (hostBuilderContext : HostBuilderContext) (serviceCollection : IServiceCollection) =
    serviceCollection.AddLogging () |> ignore
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

    serviceCollection.AddHostedService<BackgroundJob.HereticBackgroundJob> (fun x ->
        let loggerFactory = x.GetRequiredService<ILoggerFactory> ()
        let gatewayClient = x.GetRequiredService<GatewayClient> ()
        let configuration = x.GetRequiredService<IConfiguration> ()
        let ctx = createContext configuration loggerFactory
        let logger = loggerFactory.CreateLogger<BackgroundJob.HereticBackgroundJob> ()

        new BackgroundJob.HereticBackgroundJob (gatewayClient, ctx.DiscordChannelInfo, ctx.MongoDataBase, logger)
    )
    |> ignore

let builder =
    Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(fun _ config ->
            config.AddJsonFile ("local.settings.json", optional = false, reloadOnChange = true) |> ignore
        )
        .ConfigureServices (configureServices)

let host = builder.Build ()
let gatewayClient = host.Services.GetRequiredService<GatewayClient> ()
let loggerFactory = host.Services.GetRequiredService<ILoggerFactory> ()
let configuration = host.Services.GetRequiredService<IConfiguration> ()
let mongoConnectionString = configuration.GetConnectionString "MongoDb"

let ctx = createContext configuration loggerFactory

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
) |> ignore

host.AddSlashCommand ("topwords", "This command shows top 5 words for a given user", SlashCommands.topWordsCommand ctx)
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
