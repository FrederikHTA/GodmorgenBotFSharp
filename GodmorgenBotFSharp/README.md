# GodmorgenBotFSharp

A stupid little Discord bot written in F# using [NetCord](https://github.com/NetCord/NetCord) and MongoDB.
Made to gamify our daily "Godmorgen" greeting and keep track of who says it the most.

## Slash Commands

The bot supports the following slash commands:

- `/leaderboard`: Shows the leaderboard for the current month.
- `/alltimeleaderboard`: Displays the all-time leaderboard and stats for the last 3 months.
- `/wordcount`: Shows how many times a user has used specific words.
- `/topwords`: Displays the top 5 words used by a user.
- `/giveuserpoint`: Manual command to give a user a point (Restricted).
- `/giveuserpointwithwords`: Manual command to give a user a point and record words (Restricted).
- `/removepointfromuser`: Manual command to remove a point from a user (Restricted).

## Technology Stack

- **Language**: F# (.NET 10.0)
- **Discord Library**: NetCord
- **Database**: MongoDB
- **Containerization**: Docker

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- MongoDB instance
- Discord Bot Token

Run the container:
```bash
docker run -d \
  -e ConnectionStrings__MongoDb="your_mongo_connection_string" \
  -e DISCORD_TOKEN="your_discord_token" \
  -e ChannelId="your_channel_id" \
  godmorgenbot
```
