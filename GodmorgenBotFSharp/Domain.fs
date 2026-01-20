namespace GodmorgenBotFSharp.Domain

open System

type ValidationError =
    | InvalidUsername
    | InvalidCount
    | InvalidStreak
    | EmptyWord
    | InvalidWord
    | InvalidMessage

type DiscordUserId = private DiscordUserId of uint64

module DiscordUserId =
    let create (id : uint64) =
        DiscordUserId id

    let value (DiscordUserId id) = id

type DiscordUsername = private DiscordUsername of string

module DiscordUsername =
    let create (name : string) : Result<DiscordUsername, ValidationError> =
        if String.IsNullOrWhiteSpace name then
            Error ValidationError.InvalidUsername
        else
            Ok (DiscordUsername name)

    let createUnsafe (name : string) =
        DiscordUsername name

    let value (DiscordUsername name) = name

type GodmorgenCount = private GodmorgenCount of int

module GodmorgenCount =
    let create (count : int) : Result<GodmorgenCount, ValidationError> =
        if count < 0 then
            Error ValidationError.InvalidCount
        else
            Ok (GodmorgenCount count)

    let createUnsafe (count : int) =
        GodmorgenCount count

    let value (GodmorgenCount count) = count

    let increment (GodmorgenCount count) =
        GodmorgenCount (count + 1)

    let decrement (GodmorgenCount count) =
        GodmorgenCount (Math.Max (0, count - 1))

    let zero = GodmorgenCount 0

type GodmorgenStreak = private GodmorgenStreak of int

module GodmorgenStreak =
    let create (streak : int) : Result<GodmorgenStreak, ValidationError> =
        if streak < 0 then
            Error ValidationError.InvalidStreak
        else
            Ok (GodmorgenStreak streak)

    let createUnsafe (streak : int) =
        GodmorgenStreak streak

    let value (GodmorgenStreak streak) = streak

    let increment (GodmorgenStreak streak) =
        GodmorgenStreak (streak + 1)

    let decrement (GodmorgenStreak streak) =
        GodmorgenStreak (Math.Max (0, streak - 1))

    let zero = GodmorgenStreak 0

type GWord = private GWord of string

module GWord =
    let create (word : string) : Result<GWord, ValidationError> =
        let trimmed = word.Trim().ToLowerInvariant ()

        if String.IsNullOrWhiteSpace trimmed then Error ValidationError.EmptyWord
        elif trimmed.Length < 3 then Error ValidationError.InvalidWord
        elif trimmed.[0] <> 'g' then Error ValidationError.InvalidWord
        else Ok (GWord trimmed)

    let value (GWord word) = word

type MWord = private MWord of string

module MWord =
    let create (word : string) : Result<MWord, ValidationError> =
        let trimmed = word.Trim().ToLowerInvariant ()

        if String.IsNullOrWhiteSpace trimmed then Error ValidationError.EmptyWord
        elif trimmed.Length < 3 then Error ValidationError.InvalidWord
        elif trimmed.[0] <> 'm' then Error ValidationError.InvalidWord
        else Ok (MWord trimmed)

    let value (MWord word) = word

type GodmorgenMessage = {
    GWord : GWord
    MWord : MWord
}

type GodmorgenStats = {
    UserId : DiscordUserId
    Username : DiscordUsername
    LastGodmorgenDate : DateTimeOffset
    Count : GodmorgenCount
    Streak : GodmorgenStreak
}

module GodmorgenStats =
    let create (userId : DiscordUserId) (username : DiscordUsername) (now : DateTimeOffset) = {
        UserId = userId
        Username = username
        LastGodmorgenDate = now
        Count = GodmorgenCount.create 1 |> Result.defaultWith (fun _ -> GodmorgenCount.zero)
        Streak = GodmorgenStreak.create 1 |> Result.defaultWith (fun _ -> GodmorgenStreak.zero)
    }

    let hasWrittenGodmorgenToday (stats : GodmorgenStats) (now : DateTimeOffset) =
        stats.LastGodmorgenDate.Date = now.Date

    let recordGodmorgen (stats : GodmorgenStats) (now : DateTimeOffset) = {
        stats with
            LastGodmorgenDate = now
            Count = GodmorgenCount.increment stats.Count
            Streak = GodmorgenStreak.increment stats.Streak
    }
