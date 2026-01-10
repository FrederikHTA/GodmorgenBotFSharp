namespace GodmorgenBotFSharp

open Microsoft.Extensions.Logging
open MongoDB.Driver

type DiscordChannelInfo = {
    ChannelId : uint64
}

type Context = {
    MongoDataBase : IMongoDatabase
    Logger : ILogger
    DiscordChannelInfo : DiscordChannelInfo
}

