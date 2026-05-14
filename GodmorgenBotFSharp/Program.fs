open GodmorgenBotFSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open NetCord.Gateway
open NetCord.Hosting.Gateway
open NetCord.Hosting.Services.ApplicationCommands
open GodmorgenBotFSharp

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

    serviceCollection.AddHostedService<BackgroundJob.HereticBackgroundJob> (fun sp ->
        let logger = sp.GetRequiredService<ILogger<BackgroundJob.HereticBackgroundJob>> ()
        let gatewayClient = sp.GetRequiredService<GatewayClient> ()
        let configuration = sp.GetRequiredService<IConfiguration> ()
        let ctx = configuration.createContextOrThrow logger
        new BackgroundJob.HereticBackgroundJob (ctx, gatewayClient)
    )
    |> ignore

    serviceCollection.AddSingleton<Context> (fun sp ->
        let logger = sp.GetRequiredService<ILogger<Context>> ()
        let configuration = sp.GetRequiredService<IConfiguration> ()
        configuration.createContextOrThrow logger
    )
    |> ignore

let host =
    Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(fun _ config ->
            config.AddJsonFile ("appsettings.json", optional = false, reloadOnChange = true) |> ignore
            config.AddJsonFile ("local.settings.json", optional = false, reloadOnChange = true) |> ignore
        )
        .ConfigureServices(configureServices)
        .Build ()

let gatewayClient = host.Services.GetRequiredService<GatewayClient> ()
let ctx = host.Services.GetRequiredService<Context> ()

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
