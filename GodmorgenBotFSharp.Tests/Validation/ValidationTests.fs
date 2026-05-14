module GodmorgenBotFSharp.Tests.Validation

open System
open Expecto
open GodmorgenBotFSharp
open GodmorgenBotFSharp.Domain

let private utc = TimeZoneInfo.Utc

// Jan 6 2024 = Saturday, Jan 7 = Sunday, Jan 8 = Monday
let private saturday = DateTimeOffset (2024, 1, 6, 10, 0, 0, TimeSpan.Zero)
let private sunday = DateTimeOffset (2024, 1, 7, 10, 0, 0, TimeSpan.Zero)
let private monday = DateTimeOffset (2024, 1, 8, 10, 0, 0, TimeSpan.Zero)
let private at hour = DateTimeOffset (2024, 1, 8, hour, 0, 0, TimeSpan.Zero)

[<Tests>]
let tests =
    testList "Validation" [
        testList "isWeekend" [
            test "Saturday is weekend" {
                Expect.isTrue (Validation.isWeekend utc saturday) ""
            }
            test "Sunday is weekend" {
                Expect.isTrue (Validation.isWeekend utc sunday) ""
            }
            test "Monday is not weekend" {
                Expect.isFalse (Validation.isWeekend utc monday) ""
            }
        ]

        testList "isWithinGodmorgenHours" [
            test "06:00 is valid (start boundary)" {
                Expect.isTrue (Validation.isWithinGodmorgenHours utc (at 6)) ""
            }
            test "08:00 is valid" {
                Expect.isTrue (Validation.isWithinGodmorgenHours utc (at 8)) ""
            }
            test "09:00 is not valid (exclusive end)" {
                Expect.isFalse (Validation.isWithinGodmorgenHours utc (at 9)) ""
            }
            test "05:00 is not valid" {
                Expect.isFalse (Validation.isWithinGodmorgenHours utc (at 5)) ""
            }
        ]

        testList "parseGodmorgenMessage" [
            test "valid message parses g-word and m-word" {
                match Validation.parseGodmorgenMessage "godmorgen morgen" with
                | Ok msg ->
                    Expect.equal (GWord.value msg.GWord) "godmorgen" "g-word"
                    Expect.equal (MWord.value msg.MWord) "morgen" "m-word"
                | Error e -> failtest $"expected Ok, got Error {e}"
            }
            test "extra whitespace is handled" {
                Expect.isOk (Validation.parseGodmorgenMessage "  godmorgen   morgen  ") ""
            }
            test "words are normalised to lowercase" {
                match Validation.parseGodmorgenMessage "GODMORGEN MORGEN" with
                | Ok msg ->
                    Expect.equal (GWord.value msg.GWord) "godmorgen" ""
                    Expect.equal (MWord.value msg.MWord) "morgen" ""
                | Error e -> failtest $"expected Ok, got Error {e}"
            }
            test "single word returns InvalidMessage" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "godmorgen")
                    (Error ValidationError.InvalidMessage)
                    ""
            }
            test "three words returns InvalidMessage" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "god mor gen")
                    (Error ValidationError.InvalidMessage)
                    ""
            }
            test "empty string returns InvalidMessage" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "")
                    (Error ValidationError.InvalidMessage)
                    ""
            }
            test "g-word not starting with g returns InvalidWord" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "hello morgen")
                    (Error ValidationError.InvalidWord)
                    ""
            }
            test "m-word not starting with m returns InvalidWord" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "godmorgen world")
                    (Error ValidationError.InvalidWord)
                    ""
            }
            test "g-word too short returns InvalidWord" {
                Expect.equal
                    (Validation.parseGodmorgenMessage "go morgen")
                    (Error ValidationError.InvalidWord)
                    ""
            }
        ]
    ]
