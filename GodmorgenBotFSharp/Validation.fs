module GodmorgenBotFSharp.Validation

open System
open GodmorgenBotFSharp.Domain

let rst = TimeZoneInfo.FindSystemTimeZoneById "Romance Standard Time"

let isWeekend (utcNow : DateTime) =
    let day = utcNow.DayOfWeek
    day = DayOfWeek.Saturday || day = DayOfWeek.Sunday

let isWithinGodmorgenHours (utcNow : DateTime) =
    let rstNow = TimeZoneInfo.ConvertTimeFromUtc (utcNow, rst)
    rstNow.Hour >= 6 && rstNow.Hour < 9

let parseGodmorgenMessage (message : string) : Result<GodmorgenMessage, ValidationError> =
    let parts = message.Trim().Split (' ', StringSplitOptions.RemoveEmptyEntries)

    match parts with
    | [| g ; m |] ->
        match GWord.create g, MWord.create m with
        | Ok gWord, Ok mWord ->
            Ok {
                GWord = gWord
                MWord = mWord
            }
        | Error e, _ -> Error e
        | _, Error e -> Error e
    | _ -> Error ValidationError.InvalidMessage

let isValidGodmorgenMessage (message : string) =
    match parseGodmorgenMessage message with
    | Ok _ -> true
    | Error _ -> false
