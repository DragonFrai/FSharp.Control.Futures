module FSharp.Control.Futures.Tests.LazyTests

open Expecto
open FSharp.Control.Futures

let lazyCallFuncOnceTest = test "Future.lazy' correct call passed function order" {
    let checker = OrderChecker()
    let fut = Future.lazy' (fun () -> checker.PushPoint(2);)
    do checker.PushPoint(1)

    let _ = Future.Core.poll (mockContext) fut
    let _ = Future.Core.poll (mockContext) fut

    Expect.sequenceEqual (checker.ToSeq()) [1; 2] <| "Illegal breakpoint order"
    ()
}

let lazyCallWakerTest = test "Future.lazy' doesn't call waker" {
    let fut = Future.lazy' (fun () -> 0)

    let _ = Future.Core.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.lazy' shouldn't call waker")) fut
    let _ = Future.Core.poll (mockContextWithWake (fun () -> Expect.isTrue false "Future.lazy' shouldn't call waker")) fut

    ()
}

let lazyValueTest = test "Future.lazy' call passed function once" {
    let x = 12
    let fut = Future.lazy' (fun () -> x)

    let expected = Poll.Ready x
    let actual1 = Future.Core.poll (mockContext) fut
    let actual2 = Future.Core.poll (mockContext) fut

    Expect.equal actual1 expected "Future.lazy' return not passed arg or Poll.Pending on first poll"
    Expect.equal actual2 expected "Future.lazy' return not passed arg or Poll.Pending on second poll"
}

[<Tests>]
let tests =
    testList "Lazy" [
        lazyCallFuncOnceTest
        lazyCallWakerTest
        lazyValueTest
    ]
