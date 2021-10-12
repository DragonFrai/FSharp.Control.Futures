namespace FSharp.Control.Futures

open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Internals

type Future<'a> = Core.Future<'a>

[<RequireQualifiedAccess>]
module Future =

    /// <summary> Create the Computation with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Computation returned <code>Ready value</code> when polled </returns>
    let ready (value: 'a) : Future<'a> =
        Future.create
        <| fun _ctx -> Poll.Ready value
        <| fun () -> ()

    /// <summary> Create the Computation returned <code>Ready ()</code> when polled</summary>
    /// <returns> Computation returned <code>Ready ()value)</code> when polled </returns>
    let readyUnit: Future<unit> =
        Future.create
        <| fun _ctx -> Poll.Ready ()
        <| fun () -> ()

    /// <summary> Creates always pending Computation </summary>
    /// <returns> always pending Computation </returns>
    let never<'a> : Future<'a> =
        Future.create
        <| fun _ctx -> Poll.Pending
        <| fun () -> ()

    /// <summary> Creates the Computation lazy evaluator for the passed function </summary>
    /// <returns> Computation lazy evaluator for the passed function </returns>
    let lazy' (f: unit -> 'a) : Future<'a> =
        Future.create
        <| fun _ctx ->
            let x = f ()
            Poll.Ready x
        <| fun () -> ()

    [<Sealed>]
    type BindFuture<'a, 'b>(binder: 'a -> Future<'b>, source: Future<'a>) =
        let mutable fut = source
        interface Future<'b> with
            member this.Poll(ctx) =
                PollTransiting(&fut, ctx
                , onReady=fun x ->
                    let futB = binder x
                    Poll.Transit futB
                , onPending=fun () ->
                    Poll.Pending
                )
            member this.Cancel() =
                cancelIfNotNull fut

    /// <summary> Creates the Computation, asynchronously applies the result of the passed compute to the binder </summary>
    /// <returns> Computation, asynchronously applies the result of the passed compute to the binder </returns>
    let bind (binder: 'a -> Future<'b>) (source: Future<'a>) : Future<'b> =
        upcast BindFuture(binder, source)

    type MapFuture<'a, 'b>(mapping: 'a -> 'b, source: Future<'a>) =
        let mutable fut = source
        interface Future<'b> with
            member this.Poll(context) =
                PollTransiting(&fut, context
                , onReady=fun x ->
                    let r = mapping x
                    Poll.Ready r
                , onPending=fun () -> Poll.Pending
                )
            member this.Cancel() = fut.Cancel()

    /// <summary> Creates the Computation, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies mapper to result passed Computation </returns>
    let map (mapping: 'a -> 'b) (source: Future<'a>) : Future<'b> =
        upcast MapFuture(mapping, source)

    type MergeFuture<'a, 'b>(fut1: Future<'a>, fut2: Future<'b>) =
        let mutable fut1 = fut1 // if not null then r1 is undefined
        let mutable fut2 = fut2 // if not null then r2 is undefined
        let mutable r1 = Unchecked.defaultof<'a>
        let mutable r2 = Unchecked.defaultof<'b>

        interface Future<'a * 'b> with
            member this.Poll(ctx) =
                let inline complete1 r = fut1 <- Unchecked.defaultof<_>; r1 <- r
                let inline complete2 r = fut2 <- Unchecked.defaultof<_>; r2 <- r
                let inline isNotComplete (fut: Future<_>) = isNotNull fut
                let inline isBothComplete () = isNull fut1 && isNull fut2
                let inline raiseDisposing ex =
                    fut1 <- Unchecked.defaultof<_>; r1 <- Unchecked.defaultof<_>
                    fut2 <- Unchecked.defaultof<_>; r2 <- Unchecked.defaultof<_>
                    raise ex

                if isNotComplete fut1 then
                    try
                        pollTransiting fut1 ctx
                        <| fun r -> complete1 r
                        <| fun () -> ()
                        <| fun f -> fut1 <- f
                    with ex ->
                        cancelIfNotNull fut2
                        raiseDisposing ex
                if isNotComplete fut2 then
                    try
                        pollTransiting fut2 ctx
                        <| fun r -> complete2 r
                        <| fun () -> ()
                        <| fun f -> fut2 <- f
                    with ex ->
                        cancelIfNotNull fut1
                        raiseDisposing ex
                if isBothComplete () then
                    Poll.Transit (ready (r1, r2))
                else
                    Poll.Pending
            member this.Cancel() =
                cancelIfNotNull fut1
                cancelIfNotNull fut2

    /// <summary> Creates the Computation, asynchronously merging the results of passed Computations </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        upcast MergeFuture(fut1, fut2)

    type FirstFuture<'a>(fut1: Future<'a>, fut2: Future<'a>) =
        let mutable fut1 = fut1
        let mutable fut2 = fut2
        interface Future<'a> with
            member _.Poll(ctx) =
                let inline complete result =
                    fut1 <- Unchecked.defaultof<_>
                    fut2 <- Unchecked.defaultof<_>
                    Poll.Ready result
                let inline raiseDisposing ex =
                    fut1 <- Unchecked.defaultof<_>
                    fut2 <- Unchecked.defaultof<_>
                    raise ex

                try
                    pollTransiting fut1 ctx
                    <| fun x ->
                        fut2.Cancel()
                        complete x
                    <| fun () ->
                        try
                            pollTransiting fut2 ctx
                            <| fun x ->
                                fut1.Cancel()
                                complete x
                            <| fun () -> Poll.Pending
                            <| fun f -> fut2 <- f
                        with ex ->
                            fut1.Cancel()
                            raiseDisposing ex
                    <| fun f -> fut1 <- f
                with ex ->
                    fut2.Cancel()
                    raiseDisposing ex

            member _.Cancel() =
                cancelIfNotNull fut1
                cancelIfNotNull fut2

    /// <summary> Creates a Computations that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let first (fut1: Future<'a>) (fut2: Future<'a>) : Future<'a> =
        upcast FirstFuture(fut1, fut2)

    type ApplyFuture<'a, 'b>(futFun: Future<'a -> 'b>, fut: Future<'a>) =
        let mutable fut = fut // if not null then r1 is undefined
        let mutable futFun = futFun // if not null then r2 is undefined
        let mutable r1 = Unchecked.defaultof<'a>
        let mutable f2 = Unchecked.defaultof<'a -> 'b>
        interface Future<'b> with
            member _.Poll(ctx) =
                let inline complete1 r = fut <- Unchecked.defaultof<_>; r1 <- r
                let inline complete2 r = futFun <- Unchecked.defaultof<_>; f2 <- r
                let inline isNotComplete (fut: Future<_>) = isNotNull fut
                let inline isBothComplete () = isNull fut && isNull futFun
                let inline raiseDisposing ex =
                    fut <- Unchecked.defaultof<_>; r1 <- Unchecked.defaultof<_>
                    futFun <- Unchecked.defaultof<_>; f2 <- Unchecked.defaultof<_>
                    raise ex

                if isNotComplete fut then
                    try
                        pollTransiting fut ctx
                        <| fun r -> complete1 r
                        <| fun () -> ()
                        <| fun f -> fut <- f
                    with ex ->
                        cancelIfNotNull futFun
                        raiseDisposing ex
                if isNotComplete futFun then
                    try
                        pollTransiting futFun ctx
                        <| fun r -> complete2 r
                        <| fun () -> ()
                        <| fun f -> futFun <- f
                    with ex ->
                        cancelIfNotNull fut
                        raiseDisposing ex
                if isBothComplete () then
                    let r = f2 r1
                    Poll.Transit (ready r)
                else
                    Poll.Pending

            member _.Cancel() =
                cancelIfNotNull fut
                cancelIfNotNull fut

    /// <summary> Creates the Computation, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies 'f' function to result passed Computation </returns>
    let apply (futFun: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        upcast ApplyFuture(futFun, fut)

    type JoinFuture<'a>(source: Future<Future<'a>>) =
        let mutable fut = source
        interface Future<'a> with
            member this.Poll(ctx) =
                pollTransiting fut ctx
                <| fun innerFut ->
                    Poll.Transit innerFut
                <| fun () -> Poll.Pending
                <| fun f -> fut <- f
            member this.Cancel() = fut.Cancel()

    /// <summary> Creates the Computation, asynchronously joining the result of passed Computation </summary>
    /// <returns> Computation, asynchronously joining the result of passed Computation </returns>
    let join (fut: Future<Future<'a>>) : Future<'a> =
        upcast JoinFuture(fut)

    /// <summary> Create a Computation delaying invocation and computation of the Computation of the passed creator </summary>
    /// <returns> Computation delaying invocation and computation of the Computation of the passed creator </returns>
    let delay (creator: unit -> Future<'a>) : Future<'a> =
        Future.create
        <| fun _ctx ->
            let fut = creator ()
            Poll.Transit fut
        <| fun () -> ()

    type YieldWorkflowFuture() =
        let mutable isYielded = false
        interface Future<unit> with
            member this.Poll(context) =
                if isYielded then
                    Poll.Transit (ready ())
                else
                    isYielded <- true
                    context.Wake()
                    Poll.Pending
            member this.Cancel() = ()

    /// <summary> Creates a Computation that returns control flow to the scheduler once </summary>
    /// <returns> Computation that returns control flow to the scheduler once </returns>
    let yieldWorkflow () : Future<unit> =
        upcast YieldWorkflowFuture()

    [<RequireQualifiedAccess>]
    module Seq =

        open System.Collections.Generic

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let iter (seq: 'a seq) (body: 'a -> unit) =
            lazy' (fun () -> for x in seq do body x)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            let rec iterAsyncEnumerator body (enumerator: IEnumerator<'a>) =
                if enumerator.MoveNext() then
                    bind (fun () -> iterAsyncEnumerator body enumerator) (body enumerator.Current)
                else
                    readyUnit
            delay (fun () -> iterAsyncEnumerator body (source.GetEnumerator()))

    [<Struct; RequireQualifiedAccess>]
    type internal TryWithState<'body, 'handler> =
        | Empty
        | Body of body: 'body
        | Handler of handler: 'handler
        | Cancelled

    let tryWith (body: unit -> Future<'a>) (handler: exn -> Future<'a>) : Future<'a> =
        let mutable _current = TryWithState.Empty
        { new Future<_> with
            member _.Poll(ctx) =
                let rec pollCurrent () =
                    match _current with
                    | TryWithState.Empty ->
                        _current <- TryWithState.Body (body ())
                        pollCurrent ()
                    | TryWithState.Body body ->
                        try
                            Future.poll ctx body
                        with exn ->
                            _current <- TryWithState.Handler (handler exn)
                            pollCurrent ()
                    | TryWithState.Handler handler ->
                        Future.poll ctx handler
                    | TryWithState.Cancelled -> raise FutureCancelledException
                pollCurrent ()

            member _.Cancel() =
                match _current with
                | TryWithState.Empty ->
                    _current <- TryWithState.Cancelled
                | TryWithState.Body body ->
                    _current <- TryWithState.Cancelled
                    cancelIfNotNull body
                | TryWithState.Handler handler ->
                    _current <- TryWithState.Cancelled
                    cancelIfNotNull handler
                | TryWithState.Cancelled -> do ()
        }

// --------
// Builder
// --------

type FutureBuilder() =

    member inline _.Return(x): Future<'a> = Future.ready x

    member inline _.Bind(ca: Future<'a>, a2cb: 'a -> Future<'b>) = Future.bind a2cb ca

    member inline _.Zero(): Future<unit> = Future.readyUnit

    member inline _.ReturnFrom(c: Future<'a>): Future<'a> = c

    member inline this.Combine(cu: Future<unit>, u2c: unit -> Future<'a>) = this.Bind(cu, u2c)

    member inline _.MergeSources(c1: Future<'a>, c2: Future<'b>): Future<'a * 'b> =
        Future.merge c1 c2

    member inline _.Delay(u2c: unit -> Future<'a>) = u2c

    member inline _.For(source, body) = Future.Seq.iterAsync source body

    member inline this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member _.TryWith(body, handler): Future<'a> =
        Future.tryWith body handler

    member inline _.Run(u2c: unit -> Future<'a>): Future<'a> = Future.delay u2c


[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
