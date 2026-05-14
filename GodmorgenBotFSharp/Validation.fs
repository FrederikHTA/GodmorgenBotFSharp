module GodmorgenBotFSharp.Validation

open System
open GodmorgenBotFSharp.Domain

let isWeekend (utcNow : DateTimeOffset) : bool =
    let rstNow = TimeZoneInfo.ConvertTime (utcNow, Constants.romanStandardTime)
    let day = rstNow.DayOfWeek
    day = DayOfWeek.Saturday || day = DayOfWeek.Sunday

let isWithinGodmorgenHours (utcNow : DateTimeOffset) : bool =
    let rstNow = TimeZoneInfo.ConvertTime (utcNow, Constants.romanStandardTime)
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
