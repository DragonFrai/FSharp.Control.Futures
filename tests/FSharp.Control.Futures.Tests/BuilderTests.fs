module FSharp.Control.Futures.Tests.BuilderTests

open Expecto
open FSharp.Control.Futures


let returnTest = test "Builder return" {
    let fut = future {
        return 12
    }
    let patterns = [
        PollPattern.Transit
        PollPattern.Ready 12
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""
}

let returnBangTest = test "Builder return!" {
    let fut = future {
        return! Future.ready 12
    }
    let patterns = [
        PollPattern.Transit
        PollPattern.Ready 12
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""
}

let zeroTest = test "Builder zero" {
    let fut = future { () }
    let patterns = [
        PollPattern.Transit
        PollPattern.Ready ()
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""
}

let bindTest = test "Builder bind" {
    let fut = future {
        let! a = future { return 1 }
        let! b = future { return 11 }
        return a + b
    }
    let patterns = [
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Ready 12
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""
}

let mergeTest = test "Builder merge" {
    let fut = future {
        let! a = future { return 1 }
        and! b = future { return 11 }
        return a + b
    }
    let patterns = [
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Ready 12
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""
}

let forTest = test "Builder for cycle" {
    let mutable sum = 0
    let seq = [1; 2; 3; 4; 5]
    let fut = future {
        for el in seq do
            sum <- sum +  el
    }
    let patterns = [
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Transit
        PollPattern.Ready ()
    ]
    let x = runWithPatternCheck patterns fut
    Expect.equal x (Ok ()) ""

    let expected = Seq.sum seq
    Expect.equal sum expected "Future return illegal value"
}

[<Tests>]
let tests =
    testList "Future builder" [
        returnTest
        returnBangTest
        zeroTest
        bindTest
        mergeTest
        forTest
    ]
