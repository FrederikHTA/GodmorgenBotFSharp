module GodmorgenBotFSharp.Tests.Validation

open System
open Xunit
open Expecto
open GodmorgenBotFSharp
open GodmorgenBotFSharp.Domain

let private utc = TimeZoneInfo.Utc

// Jan 6 2024 = Saturday, Jan 7 = Sunday, Jan 8 = Monday
let private saturday = DateTimeOffset (2024, 1, 6, 10, 0, 0, TimeSpan.Zero)
let private sunday = DateTimeOffset (2024, 1, 7, 10, 0, 0, TimeSpan.Zero)
let private monday = DateTimeOffset (2024, 1, 8, 10, 0, 0, TimeSpan.Zero)
let private at hour = DateTimeOffset (2024, 1, 8, hour, 0, 0, TimeSpan.Zero)

[<Fact>]
let ``isWeekend - Saturday is weekend`` () =
    Expect.isTrue (Validation.isWeekend utc saturday) ""

[<Fact>]
let ``isWeekend - Sunday is weekend`` () =
    Expect.isTrue (Validation.isWeekend utc sunday) ""

[<Fact>]
let ``isWeekend - Monday is not weekend`` () =
    Expect.isFalse (Validation.isWeekend utc monday) ""

[<Fact>]
let ``isWithinGodmorgenHours - 06:00 is valid (start boundary)`` () =
    Expect.isTrue (Validation.isWithinGodmorgenHours utc (at 6)) ""

[<Fact>]
let ``isWithinGodmorgenHours - 08:00 is valid`` () =
    Expect.isTrue (Validation.isWithinGodmorgenHours utc (at 8)) ""

[<Fact>]
let ``isWithinGodmorgenHours - 09:00 is not valid (exclusive end)`` () =
    Expect.isFalse (Validation.isWithinGodmorgenHours utc (at 9)) ""

[<Fact>]
let ``isWithinGodmorgenHours - 05:00 is not valid`` () =
    Expect.isFalse (Validation.isWithinGodmorgenHours utc (at 5)) ""

[<Fact>]
let ``parseGodmorgenMessage - valid message parses g-word and m-word`` () =
    match Validation.parseGodmorgenMessage "godmorgen morgen" with
    | Ok msg ->
        Expect.equal (GWord.value msg.GWord) "godmorgen" "g-word"
        Expect.equal (MWord.value msg.MWord) "morgen" "m-word"
    | Error e -> failtest $"expected Ok, got Error {e}"

[<Fact>]
let ``parseGodmorgenMessage - extra whitespace is handled`` () =
    Expect.isOk (Validation.parseGodmorgenMessage "  godmorgen   morgen  ") ""

[<Fact>]
let ``parseGodmorgenMessage - words are normalised to lowercase`` () =
    match Validation.parseGodmorgenMessage "GODMORGEN MORGEN" with
    | Ok msg ->
        Expect.equal (GWord.value msg.GWord) "godmorgen" ""
        Expect.equal (MWord.value msg.MWord) "morgen" ""
    | Error e -> failtest $"expected Ok, got Error {e}"

[<Fact>]
let ``parseGodmorgenMessage - single word returns InvalidMessage`` () =
    Expect.equal (Validation.parseGodmorgenMessage "godmorgen") (Error ValidationError.InvalidMessage) ""

[<Fact>]
let ``parseGodmorgenMessage - three words returns InvalidMessage`` () =
    Expect.equal (Validation.parseGodmorgenMessage "god mor gen") (Error ValidationError.InvalidMessage) ""

[<Fact>]
let ``parseGodmorgenMessage - empty string returns InvalidMessage`` () =
    Expect.equal (Validation.parseGodmorgenMessage "") (Error ValidationError.InvalidMessage) ""

[<Fact>]
let ``parseGodmorgenMessage - g-word not starting with g returns InvalidWord`` () =
    Expect.equal (Validation.parseGodmorgenMessage "hello morgen") (Error ValidationError.InvalidWord) ""

[<Fact>]
let ``parseGodmorgenMessage - m-word not starting with m returns InvalidWord`` () =
    Expect.equal (Validation.parseGodmorgenMessage "godmorgen world") (Error ValidationError.InvalidWord) ""

[<Fact>]
let ``parseGodmorgenMessage - g-word too short returns InvalidWord`` () =
    Expect.equal (Validation.parseGodmorgenMessage "go morgen") (Error ValidationError.InvalidWord) ""
