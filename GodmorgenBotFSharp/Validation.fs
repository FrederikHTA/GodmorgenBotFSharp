module GodmorgenBotFSharp.Validation

open System

let rst = TimeZoneInfo.FindSystemTimeZoneById "Romance Standard Time"

let isWeekend (utcNow : DateTime) =
    let day = utcNow.DayOfWeek
    day = DayOfWeek.Saturday || day = DayOfWeek.Sunday

let isWithinGodmorgenHours (utcNow : DateTime) =
    let rstNow = TimeZoneInfo.ConvertTimeFromUtc (utcNow, rst)
    rstNow.Hour >= 6 && rstNow.Hour < 9

let isValidGodmorgenMessage (message : string) =
    let trimmedMessage = message.Trim().ToLowerInvariant().Split ' '

    match trimmedMessage with
    | [| gWord ; mWord |] when gWord[0] = 'g' && mWord[0] = 'm' -> true
    | _ -> false

let validateWord (word : string) (expectedFirstChar : char) =
    let trimmed = word.Trim().ToLowerInvariant()
    if (String.IsNullOrWhiteSpace word) then
        Error $"Invalid word format. Expected word starting with '{expectedFirstChar}' empty string."
    else if trimmed[0] <> expectedFirstChar then
        Error $"Invalid word format. Expected word starting with '{expectedFirstChar}' but got '{trimmed[0]}'."
    else
        Ok ()
