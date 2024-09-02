[<AutoOpen>]
module Utils

open System.Collections.Generic
open System.Diagnostics
open System.Reflection
open System.Threading
open FSharp.Control.Futures
open System.Collections.Concurrent
open FSharp.Control.Futures.LowLevel
open Xunit.Sdk

type OrderChecker() =
    let points = ConcurrentBag<int>()

    member _.PushPoint(pointId: int) : unit =
        points.Add(pointId)

    member _.ToSeq() : int seq =
        Seq.ofArray (points.ToArray() |> Array.rev)

    member this.Check(points': int seq) : bool =
        let points = this.ToSeq()
        (points, points') ||> Seq.forall2 (=)


type SpinCondVar =
    val mutable allowed: int
    new() = { allowed = 0 }

    member inline this.Set(): unit =
        this.allowed <- 1

    member inline this.Wait(): unit =
        while this.allowed = 0 do
            ()

[<RequireQualifiedAccess>]
module Context =

    [<Class>]
    type MockContext =
        val mutable state: int
        val mutable wake: (unit -> unit) option

        new() = { state = 0; wake = None }
        new(wake: unit -> unit) = { state = 0; wake = Some wake }

        member this.Waked: int = this.state

        /// <summary>
        /// Reset Waked counter to 0 and return wake count
        /// </summary>
        member this.Reset(): int =
            let prev = Interlocked.Exchange(&this.state, 0)
            prev

        interface IContext with
            member this.Wake() =
                Interlocked.Increment(&this.state) |> ignore

    let mockContext () : MockContext = MockContext()
    let mockContextWithWake (wake: unit -> unit) : MockContext = MockContext(wake)

    let nonAwakenedContext : IContext =
        { new IContext with
            member _.Wake() = invalidOp "Context was wake" }


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
module Tests =
    let repeat (n: int) (f: unit -> unit) =
        for _ in 1..n do f ()


[<RequireQualifiedAccess>]
module Result =
    let get (r: Result<'a, 'e>) = match r with Ok x -> x | Error e -> failwith $"{e}"

type RepeatAttribute =
    inherit DataAttribute

    val mutable private count: int

    new(count: int) =
        Trace.Assert(count >= 1, "Repeat count must be >= 1")
        { count = count }

    override this.GetData(_methodInfo: MethodInfo) : obj array seq =
        Seq.init this.count (fun _i -> [| |])


type TestFutureTask<'a> =
    val mutable private isInitial: bool
    val private context: Context.MockContext
    val mutable private fut: NaiveFuture<'a>
    new(fut: Future<'a>) =
        {
            isInitial = true
            context = Context.MockContext()
            fut = NaiveFuture(fut)
        }

    member this.IsWaked: bool = this.context.Waked <> 0

    member this.Poll(assertWaked: bool): NaivePoll<'a> =
        let wakes = this.context.Reset()
        match (this.isInitial, wakes) with
        | true, _ ->
            this.isInitial <- false
            this.fut.Poll(this.context)
        | false, 0 when assertWaked ->
            failwith "TestFutureTask polled repeatedly without waking"
        | false, _ ->
            this.fut.Poll(this.context)

    member this.Poll(): NaivePoll<'a> =
        this.Poll(true)

    member this.Drop(): unit =
        this.fut.Drop()
