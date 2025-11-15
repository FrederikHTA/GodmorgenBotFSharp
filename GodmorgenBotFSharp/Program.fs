open System.Threading.Tasks
open GodmorgenBotFSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MongoDB.Driver
open NetCord
open NetCord.Gateway
open NetCord.Hosting.Gateway
open NetCord.Hosting.Services.ApplicationCommands

let builder =
    Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(fun context config -> config.AddJsonFile ("local.settings.json", optional = false, reloadOnChange = true) |> ignore)
        .ConfigureServices (fun hostBuilderContext serviceCollection ->
            serviceCollection.AddLogging() |> ignore
            serviceCollection.AddHostedService<BackgroundTaskTest.BackgroundWorker>() |> ignore
            serviceCollection.AddDiscordGateway () |> ignore
        )

let host = builder.Build ()

let gatewayClient = host.Services.GetRequiredService<GatewayClient> ()
let loggerFactory = host.Services.GetRequiredService<ILoggerFactory>()
let logger = loggerFactory.CreateLogger("GodmorgenBot")
let configuration = host.Services.GetRequiredService<IConfiguration>()
let mongoConnectionString = configuration.GetConnectionString("MongoDb")

let mongoClient = new MongoClient(mongoConnectionString)
let database = mongoClient.GetDatabase("godmorgen")

gatewayClient.add_MessageCreate (fun message ->
    task {
        if message.Author.IsBot then
            return ()
        else
            let! _ = message.ReplyAsync $"Hello, {message.Author.Username}!"
            return ()
    } |> ValueTask
)

type Delegate = delegate of User -> string
let delegateFunc = Delegate (fun user -> $"Pong! <@{user.Id}>")
host.AddSlashCommand("ping", "Replies with Pong!", delegateFunc) |> ignore

host.RunAsync () |> Async.AwaitTask |> Async.RunSynchronously
