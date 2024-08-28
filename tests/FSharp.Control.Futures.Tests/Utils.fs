[<AutoOpen>]
module Utils

open System.Threading
open FSharp.Control.Futures
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
        member _.Wake() = invalidOp "Context was wake" }
let mockContext: IContext =
    { new IContext with
        member _.Wake() = do () }
let mockContextWithWake (wake: unit -> unit) =
    { new IContext with
        member _.Wake() = wake () }


[<RequireQualifiedAccess>]
type PollPattern<'a> =
    | Ready of 'a
    | Pending
    | Transit

let runWithPatternCheck (patterns: PollPattern<'a> list) (fut: Future<'a>) : Result<unit, string> =
    use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
    // TODO: Add checks not call Wake if pattern not contains Pending
    let ctx = { new IContext with member _.Wake() = wh.Set() |> ignore }
    let rec pollLoop (patterns: PollPattern<'a> list) (fut: Future<'a>) =
        match patterns with
        | [] -> failwith "Future not ready, but patterns is empty"
        | pattern :: patterns ->
            let futRes = fut.Poll(ctx)
            match pattern, futRes with
            | PollPattern.Ready _, _ when not (List.isEmpty patterns) ->
                Error "Ready pattern not last in pattern seq"
            | PollPattern.Ready x, Poll.Ready y when x = y ->
                Ok ()
            | PollPattern.Ready x, Poll.Ready y when x <> y ->
                Error $"Expected future result {x} <> actual {y}"
            | PollPattern.Pending, Poll.Pending ->
                wh.WaitOne() |> ignore
                pollLoop patterns fut
            | PollPattern.Transit, Poll.Transit fut ->
                pollLoop patterns fut
            | expectedPattern, actual ->
                Error $"Polling result {actual} not compatible with expected {expectedPattern}"
    pollLoop patterns fut


[<RequireQualifiedAccess>]
module Result =
    let get (r: Result<'a, 'e>) = match r with Ok x -> x | Error e -> failwith $"{e}"
