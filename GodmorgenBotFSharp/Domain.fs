namespace GodmorgenBotFSharp.Domain

open System

[<RequireQualifiedAccess>]
type ValidationError =
    | InvalidCount
    | InvalidStreak
    | EmptyWord
    | InvalidWord
    | InvalidMessage

type DiscordUserId = private DiscordUserId of uint64

module DiscordUserId =
    let create (id : uint64) : DiscordUserId =
        DiscordUserId id

    let value (DiscordUserId id) : uint64 = id

type GodmorgenCount = private GodmorgenCount of int

module GodmorgenCount =
    let create (count : int) : Result<GodmorgenCount, ValidationError> =
        if count < 0 then
            Error ValidationError.InvalidCount
        else
            Ok (GodmorgenCount count)

    let createUnsafe (count : int) : GodmorgenCount =
        GodmorgenCount count

    let value (GodmorgenCount count) : int = count

    let increment (GodmorgenCount count) : GodmorgenCount =
        GodmorgenCount (count + 1)

    let decrement (GodmorgenCount count) : GodmorgenCount =
        GodmorgenCount (Math.Max (0, count - 1))

type GodmorgenStreak = private GodmorgenStreak of int

module GodmorgenStreak =
    let create (streak : int) : Result<GodmorgenStreak, ValidationError> =
        if streak < 0 then
            Error ValidationError.InvalidStreak
        else
            Ok (GodmorgenStreak streak)

    let createUnsafe (streak : int) : GodmorgenStreak =
        GodmorgenStreak streak

    let value (GodmorgenStreak streak) : int = streak

    let increment (GodmorgenStreak streak) : GodmorgenStreak =
        GodmorgenStreak (streak + 1)

    let decrement (GodmorgenStreak streak) : GodmorgenStreak =
        GodmorgenStreak (Math.Max (0, streak - 1))

type GWord = private GWord of string

module GWord =
    let create (word : string) : Result<GWord, ValidationError> =
        let trimmed = word.Trim().ToLowerInvariant ()

        if String.IsNullOrWhiteSpace trimmed then Error ValidationError.EmptyWord
        elif trimmed.Length < 3 then Error ValidationError.InvalidWord
        elif trimmed.[0] <> 'g' then Error ValidationError.InvalidWord
        else Ok (GWord trimmed)

    let value (GWord word) : string = word

type MWord = private MWord of string

module MWord =
    let create (word : string) : Result<MWord, ValidationError> =
        let trimmed = word.Trim().ToLowerInvariant ()

        if String.IsNullOrWhiteSpace trimmed then Error ValidationError.EmptyWord
        elif trimmed.Length < 3 then Error ValidationError.InvalidWord
        elif trimmed.[0] <> 'm' then Error ValidationError.InvalidWord
        else Ok (MWord trimmed)

    let value (MWord word) : string = word

type GodmorgenMessage = {
    GWord : GWord
    MWord : MWord
}

type GodmorgenStats = {
    UserId : DiscordUserId
    LastGodmorgenDate : DateTimeOffset
    Count : GodmorgenCount
    Streak : GodmorgenStreak
}

module GodmorgenStats =
    let create (userId : DiscordUserId) (now : DateTimeOffset) : GodmorgenStats = {
        UserId = userId
        LastGodmorgenDate = now
        Count = GodmorgenCount.createUnsafe 1
        Streak = GodmorgenStreak.createUnsafe 1
    }

    let hasWrittenGodmorgenToday (now : DateTimeOffset) (stats : GodmorgenStats) : bool =
        stats.LastGodmorgenDate.Date = now.Date

    let updateLastGodmorgenDate (now : DateTimeOffset) (stats : GodmorgenStats) : GodmorgenStats = {
        stats with
            LastGodmorgenDate = now
    }

    let incrementGodmorgenCount (stats : GodmorgenStats) : GodmorgenStats = {
        stats with
            Count = GodmorgenCount.increment stats.Count
            Streak = GodmorgenStreak.increment stats.Streak
    }

    let decreaseGodmorgenCount (stats : GodmorgenStats) : GodmorgenStats = {
        stats with
            Count = GodmorgenCount.decrement stats.Count
            Streak = GodmorgenStreak.decrement stats.Streak
    }
