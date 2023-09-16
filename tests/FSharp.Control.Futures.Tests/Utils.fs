[<AutoOpen>]
module Utils

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


let nonAwakenedContext: IContext =
    { new IContext with
        member _.Wake() = invalidOp "Context was wake"
        member _.Scheduler = None }
let mockContext: IContext =
    { new IContext with
        member _.Wake() = do ()
        member _.Scheduler = None }
let mockContextWithWake (wake: unit -> unit) =
    { new IContext with
        member _.Wake() = wake ()
        member _.Scheduler = None }

