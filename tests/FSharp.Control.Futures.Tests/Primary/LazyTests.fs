module FSharp.Control.Futures.Tests.LazyTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel

let lazyCallFuncOnceTest = test "Future.lazy' correct call passed function order" {
    let checker = OrderChecker()
    let fut = Future.lazy' (fun () -> checker.PushPoint(2);)
    do checker.PushPoint(1)

    let _ = Future.poll (mockContext) fut

    Expect.sequenceEqual (checker.ToSeq()) [1; 2] <| "Illegal breakpoint order"
    ()
}

let lazyCallWakerTest = test "Future.lazy' doesn't call waker" {
    let fut = Future.lazy' (fun () -> 0)

    let _ = Future.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.lazy' shouldn't call waker")) fut
    ()
}

let lazyValueTest = test "Future.lazy' call passed function once" {
    let x = 12
    let fut = Future.lazy' (fun () -> x)

    let expected = Poll.Ready x
    let actual = Future.poll (mockContext) fut

    Expect.equal actual expected "Future.lazy' return not passed arg or Poll.Pending on poll"
}

[<Tests>]
let tests =
    testList "Future.lazy'" [
        lazyCallFuncOnceTest
        lazyCallWakerTest
        lazyValueTest
    ]
