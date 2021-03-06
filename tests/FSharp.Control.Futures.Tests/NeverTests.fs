module FSharp.Control.Futures.Tests.NeverTests

open Expecto
open FSharp.Control.Futures


let neverValueTest = test "Future.never future returns Pending" {
    let fut: Future<int> = Future.never ()

    let expected = Poll.Pending
    let actual1 = Future.Core.poll (mockContext) fut
    let actual2 = Future.Core.poll (mockContext) fut

    Expect.equal actual1 expected "Future.never return Ready on first poll"
    Expect.equal actual2 expected "Future.ready return Ready on second poll"
}

let neverWakerTest = test "Future.never future doesn't call waker" {
    let fut = Future.ready ()

    let _ = Future.Core.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.never shouldn't call waker on first poll")) fut
    let _ = Future.Core.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.never shouldn't call waker on second poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Never" [
        neverValueTest
        neverWakerTest
    ]
