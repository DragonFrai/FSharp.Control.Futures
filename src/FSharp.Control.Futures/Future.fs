namespace FSharp.Control.Futures

open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Internals

type Future<'a> = Core.Future<'a>

[<RequireQualifiedAccess>]
module Futures =

    [<Sealed>]
    type Ready<'a>(value: 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready value
            member this.Cancel() = ()

    [<Sealed>]
    type ReadyUnit private () =
        static member Instance : ReadyUnit = ReadyUnit()
        interface Future<unit> with
            member this.Poll(_ctx) = Poll.Ready ()
            member this.Cancel() = ()

    [<Sealed>]
    type Never<'a> private () =
        static member Create<'a>() : Never<'a> = Never()
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Pending
            member this.Cancel() = ()

    [<Sealed>]
    type Lazy<'a>(lazy': unit -> 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready (lazy' ())
            member this.Cancel() = ()

    [<Sealed>]
    type Delay<'a>(delay: unit -> Future<'a>) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Transit (delay ())
            member this.Cancel() = ()

    [<Sealed>]
    type Yield() =
        let mutable isYielded = false
        interface Future<unit> with
            member this.Poll(ctx) =
                if isYielded
                then Poll.Ready ()
                else
                    isYielded <- true
                    ctx.Wake()
                    Poll.Pending
            member this.Cancel() = ()

    [<Sealed>]
    type Bind<'a, 'b>(source: Future<'a>, binder: 'a -> Future<'b>) =
        let mutable poller = NaivePoller(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Transit (binder result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Cancel() = poller.Cancel()

    [<Sealed>]
    type Map<'a, 'b>(mapping: 'a -> 'b, source: Future<'a>) =
        let mutable poller = NaivePoller(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Ready (mapping result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Cancel() = poller.Cancel()

    [<Sealed>]
    type Merge<'a, 'b>(fut1: Future<'a>, fut2: Future<'b>) =
        let mutable sMerge = StructuralMerge(fut1, fut2)

        interface Future<'a * 'b> with
            member this.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (a, b)) -> Poll.Ready (a, b)
                | NaivePoll.Pending -> Poll.Pending

            member this.Cancel() =
                sMerge.Cancel()

    [<Sealed>]
    type First<'a>(fut1: Future<'a>, fut2: Future<'a>) =
        let mutable poller1 = NaivePoller(fut1)
        let mutable poller2 = NaivePoller(fut2)

        interface Future<'a> with
            member _.Poll(ctx) =
                try
                    match poller1.Poll(ctx) with
                    | NaivePoll.Ready result ->
                        poller2.Cancel()
                        Poll.Ready result
                    | NaivePoll.Pending ->
                        try
                            match poller2.Poll(ctx) with
                            | NaivePoll.Ready result ->
                                poller1.Cancel()
                                Poll.Ready result
                            | NaivePoll.Pending ->
                                Poll.Pending
                        with ex ->
                            poller2.Terminate()
                            poller1.Cancel()
                            raise ex
                with ex ->
                    poller1.Terminate()
                    poller2.Cancel()
                    raise ex

            member _.Cancel() =
                poller1.Cancel()
                poller2.Cancel()

    [<Sealed>]
    type Join<'a>(source: Future<Future<'a>>) =
        let mutable poller = NaivePoller(source)
        interface Future<'a> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready innerFut -> Poll.Transit innerFut
                | NaivePoll.Pending -> Poll.Pending
            member this.Cancel() = poller.Cancel()

    [<Sealed>]
    type Apply<'a, 'b>(fut: Future<'a>, futFun: Future<'a -> 'b>) =
        let mutable sMerge = StructuralMerge(fut, futFun)

        interface Future<'b> with
            member _.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (x, f)) -> Poll.Ready (f x)
                | NaivePoll.Pending -> Poll.Pending

            member _.Cancel() =
                sMerge.Cancel()

[<RequireQualifiedAccess>]
module Future =

    /// <summary> Create the Computation with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Computation returned <code>Ready value</code> when polled </returns>
    let inline ready (value: 'a) : Future<'a> =
        upcast Futures.Ready(value)

    /// <summary> Create the Computation returned <code>Ready ()</code> when polled</summary>
    /// <returns> Computation returned <code>Ready ()value)</code> when polled </returns>
    let readyUnit: Future<unit> =
        upcast Futures.ReadyUnit.Instance

    /// <summary> Creates always pending Computation </summary>
    /// <returns> always pending Computation </returns>
    let never<'a> : Future<'a> =
        upcast Futures.Never.Create()

    /// <summary> Creates the Computation lazy evaluator for the passed function </summary>
    /// <returns> Computation lazy evaluator for the passed function </returns>
    let inline lazy' (f: unit -> 'a) : Future<'a> =
        upcast Futures.Lazy(f)

    /// <summary> Creates the Computation, asynchronously applies the result of the passed compute to the binder </summary>
    /// <returns> Computation, asynchronously applies the result of the passed compute to the binder </returns>
    let inline bind (binder: 'a -> Future<'b>) (source: Future<'a>) : Future<'b> =
        upcast Futures.Bind(source, binder)

    /// <summary> Creates the Computation, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies mapper to result passed Computation </returns>
    let inline map (mapping: 'a -> 'b) (source: Future<'a>) : Future<'b> =
        upcast Futures.Map(mapping, source)

    /// <summary> Creates the Computation, asynchronously merging the results of passed Computations </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let inline merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        upcast Futures.Merge(fut1, fut2)

    /// <summary> Creates a Computations that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let inline first (fut1: Future<'a>) (fut2: Future<'a>) : Future<'a> =
        upcast Futures.First(fut1, fut2)

    /// <summary> Creates the Computation, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies 'f' function to result passed Computation </returns>
    let inline apply (futFun: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        upcast Futures.Apply(fut, futFun)

    /// <summary> Creates the Computation, asynchronously joining the result of passed Computation </summary>
    /// <returns> Computation, asynchronously joining the result of passed Computation </returns>
    let inline join (fut: Future<Future<'a>>) : Future<'a> =
        upcast Futures.Join(fut)

    /// <summary> Create a Computation delaying invocation and computation of the Computation of the passed creator </summary>
    /// <returns> Computation delaying invocation and computation of the Computation of the passed creator </returns>
    let inline delay (creator: unit -> Future<'a>) : Future<'a> =
        upcast Futures.Delay(creator)

    /// <summary> Creates a Computation that returns control flow to the scheduler once </summary>
    /// <returns> Computation that returns control flow to the scheduler once </returns>
    let inline yieldWorkflow () : Future<unit> =
        upcast Futures.Yield()

    [<RequireQualifiedAccess>]
    module Seq =

        open System.Collections.Generic

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let iterBlocking (seq: 'a seq) (body: 'a -> unit) =
            lazy' (fun () -> for x in seq do body x)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let iter (source: 'a seq) (body: 'a -> Future<unit>) =
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

    member inline _.For(source, body) = Future.Seq.iter source body

    member inline this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member _.TryWith(body, handler): Future<'a> =
        Future.tryWith body handler

    member inline _.Run(u2c: unit -> Future<'a>): Future<'a> = Future.delay u2c


[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
