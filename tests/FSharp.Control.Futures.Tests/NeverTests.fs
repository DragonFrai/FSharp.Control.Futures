module FSharp.Control.Futures.Tests.NeverTests

open Expecto
open FSharp.Control.Futures


let neverValueTest = test "Future.never future returns Pending" {
    let fut: Future<int> = Future.never

    let expected = Poll.Pending
    let actual = Future.poll (mockContext) fut

    Expect.equal actual expected "Future.never don't return Pending on poll"
}

let neverWakerTest = test "Future.never future doesn't call waker" {
    let fut = Future.never

    let _ = Future.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.never shouldn't call waker on poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Future.never" [
        neverValueTest
        neverWakerTest
    ]
