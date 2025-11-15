module GodmorgenBotFSharp.BackgroundTaskTest

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

type BackgroundWorker (logger : ILogger<BackgroundWorker>) =
    inherit BackgroundService ()

    override _.ExecuteAsync (token : CancellationToken) =
        task {
            logger.LogInformation "Launching 🚀"

            use timer =
                new Timer ((fun _ -> logger.LogInformation $"Running {DateTime.Now:T}"), null, TimeSpan.Zero, TimeSpan.FromSeconds 5.0)

            do! Task.Delay (Timeout.Infinite, token)

            logger.LogInformation "Cleaning Up 🧹"
            logger.LogInformation "Quitting 👋"
        }
