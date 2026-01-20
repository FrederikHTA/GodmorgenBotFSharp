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

let validateWord (word : string) (expectedFirstChar : char) =
    let wordLower = word.ToLowerInvariant ()

    match expectedFirstChar with
    | 'g' -> GWord.create wordLower |> Result.map ignore
    | 'm' -> MWord.create wordLower |> Result.map ignore
    | _ ->
        // Fallback for cases not covered by domain types
        let trimmed = wordLower.Trim ()

        if String.IsNullOrWhiteSpace trimmed then
            Error
                $"Invalid word format. Expected word starting with '{expectedFirstChar}' but got empty string."
        elif trimmed.[0] <> expectedFirstChar then
            Error
                $"Invalid word format. Expected word starting with '{expectedFirstChar}' but got '{trimmed.[0]}'."
        else
            Ok ()
