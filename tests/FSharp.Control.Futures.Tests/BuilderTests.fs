module FSharp.Control.Futures.Tests.BuilderTests
//
//open Expecto
//open FSharp.Control.Futures
//
//
//let returnTest = test "Builder return" {
//    let fut = future { return 12 }
//    let x = Future.Core.poll nonAwakenedContext fut
//    Expect.equal x (Poll.Ready 12) "Future return illegal value"
//}
//
//let returnBangTest = test "Builder return!" {
//    let fut = future { return! Future.ready 12 }
//    let x = Future.Core.poll nonAwakenedContext fut
//    Expect.equal x (Poll.Ready 12) "Future return illegal value"
//}
//
//let zeroTest = test "Builder zero" {
//    let fut = future { () }
//    let x = Future.Core.poll nonAwakenedContext fut
//    Expect.equal x (Poll.Ready ()) "Future return illegal value"
//}
//
//let bindTest = test "Builder bind" {
//    let fut = future {
//        let! a = future { return 1 }
//        let! b = future { return 11 }
//        return a + b
//    }
//    let x = Future.Core.poll nonAwakenedContext fut
//    Expect.equal x (Poll.Ready 12) "Future return illegal value"
//}
//
//let mergeTest = test "Builder merge" {
//    let fut = future {
//        let! a = future { return 1 }
//        and! b = future { return 11 }
//        return a + b
//    }
//    let x = Future.Core.poll nonAwakenedContext fut
//    Expect.equal x (Poll.Ready 12) "Future return illegal value"
//}
//
//let forTest = test "Builder for cycle" {
//    let mutable sum = 0
//    let seq = [1; 2; 3; 4; 5]
//    let fut = future {
//        for el in seq do
//            sum <- sum +  el
//    }
//    let _ = Future.Core.poll nonAwakenedContext fut
//    let expected = Seq.sum seq
//    Expect.equal sum expected "Future return illegal value"
//}
//
//[<Tests>]
//let tests =
//    testList "Future builder" [
//        returnTest
//        returnBangTest
//        zeroTest
//        bindTest
//        mergeTest
//        forTest
//    ]
