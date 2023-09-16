module FSharp.Control.Futures.Tests.ReadyTests

open Expecto
open FSharp.Control.Futures


let readyValueTest = test "Future.ready future returns passed arg" {
    let x = 12
    let fut = Future.ready x

    let expected = Poll.Ready x
    let actual = Future.poll mockContext fut

    Expect.equal actual expected "Future.ready return not passed arg or Pending on poll"
}

let readyWakerTest = test "Future.ready future doesn't call waker" {
    let fut = Future.ready ()

    let _ = Future.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.ready shouldn't call waker on poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Future.ready" [
        readyValueTest
        readyWakerTest
    ]
