// For more information see https://aka.ms/fsharp-console-apps
open Microsoft.Extensions.Hosting
open NetCord.Hosting.Gateway

let builder = Host.CreateApplicationBuilder ()

builder.Services.AddDiscordGateway () |> ignore

let host = builder.Build ()

host.RunAsync () |> Async.AwaitTask |> Async.RunSynchronously
