module FSharp.Control.Futures.Tests.PointOrder

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Tests.Utils



let delayTest () =
    let checker = OrderChecker()

    let fut = future {
        do checker.PushPoint(1)
        do checker.PushPoint(2)
    }
    do checker.PushPoint(3)
    fut |> Future.run
    Expect.isTrue <| checker.Check([3; 1; 2]) <| "Illegal breakpoint order"


[<Tests>]
let tests =
  testList "Points order" [
      testCase "delay check" <| delayTest
  ]
