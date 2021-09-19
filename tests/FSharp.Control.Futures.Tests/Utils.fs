[<AutoOpen>]
module Utils

open System.Collections.Concurrent
open FSharp.Control.Futures.Core

type OrderChecker() =
    let points = ConcurrentBag<int>()

    member _.PushPoint(pointId: int) : unit =
        points.Add(pointId)

    member _.ToSeq() : int seq =
        Seq.ofArray (points.ToArray() |> Array.rev)

    member this.Check(points': int seq) : bool =
        let points = this.ToSeq()
        (points, points') ||> Seq.forall2 (=)


let nonAwakenedContext: Context = { new Context() with member _.Wake() = invalidOp "Context was wake" }
let mockContext: Context = { new Context() with member _.Wake() = do () }
let mockContextWithWake (wake: unit -> unit) = { new Context() with member _.Wake() = wake () }

