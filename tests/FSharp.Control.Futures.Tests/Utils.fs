module FSharp.Control.Futures.Tests.Utils

open System.Collections.Concurrent

type OrderChecker() =
    let points = ConcurrentBag<int>()

    member _.PushPoint(pointId: int) : unit =
        points.Add(pointId)

    member _.ToSeq() : int seq =
        Seq.ofArray (points.ToArray() |> Array.rev)

    member this.Check(points': int seq) : bool =
        let points = this.ToSeq()
        (points, points') ||> Seq.forall2 (=)



