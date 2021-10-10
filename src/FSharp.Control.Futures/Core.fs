namespace FSharp.Control.Futures.Core

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Threading


/// <summary> Current state of a AsyncComputation </summary>
type [<Struct; RequireQualifiedAccess>]
    Poll<'a> =
    | Ready of readyValue: 'a
    | Pending
    | Transit of transitComputation: IFuture<'a>

/// # IAsyncComputation poll schema
/// [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
and IFuture<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    //[<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll: context: IContext -> Poll<'a>

    /// <summary> Cancel asynchronously Computation computation </summary>
    /// <remarks> Notifies internal asynchronous operations of Computation cancellations. </remarks>
    //[<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

and Future<'a> = IFuture<'a>

/// <summary> The context of the running computation.
/// Allows the computation to signal its ability to move forward (awake) through the Wake method </summary>
and IContext =
    /// <summary> Wake up assigned Future </summary>
    abstract Wake: unit -> unit
    /// Current scheduler
    abstract Scheduler: IScheduler option

/// <summary> Scheduler Future. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread).  </summary>
and IScheduler =
    inherit IDisposable
    /// IScheduler.Spawn принимает Future, так как вызов Future.RunComputation является частью асинхронного вычисления.
    abstract Spawn: Future<'a> -> IJoinHandle<'a>

/// <summary> Allows to cancel and wait (asynchronously or synchronously) for a spawned Future. </summary>
and IJoinHandle<'a> =
    abstract Cancel: unit -> unit
    abstract Join: unit -> 'a
    abstract Await: unit -> Future<'a>

// and [<Interface>]
//     Future_OLD<'a> =
//     /// <summary> starts execution of the current Future and returns its "tail" as IAsyncComputation. </summary>
//     /// <remarks> The call to Future.RunComputation is part of the asynchronous computation.
//     /// And it should be call in an asynchronous context. </remarks>
//     // [<EditorBrowsable(EditorBrowsableState.Advanced)>]
//     abstract StartComputation: unit -> Future<'a>
// and Future_OLD<'a> = Future_OLD<'a>

/// Exception is thrown when re-polling after cancellation (assuming IAsyncComputation is tracking such an invalid call)
exception FutureCancelledException

[<RequireQualifiedAccess>]
module Future =

    //#region Core
    let inline cancelIfNotNull (comp: Future<'a>) =
        if isNotNull comp then comp.Cancel()

    let inline cancel (comp: Future<'a>) =
        comp.Cancel()

    // /// <summary> Create a Computation with members from passed functions </summary>
    // /// <param name="poll"> Poll body </param>
    // /// <param name="cancel"> Poll body </param>
    // /// <returns> Computation implementations with passed members </returns>
    // let inline create ([<InlineIfLambda>] poll: IContext -> Poll<'a>) ([<InlineIfLambda>] cancel: unit -> unit) : Future<'a> =
    //     { new Future<'a> with
    //         member this.Poll(context) = poll context
    //         member this.Cancel() = cancel () }

    let inline poll context (comp: Future<'a>) = comp.Poll(context)

    [<AutoOpen>]
    type Helpers =
        // NOTE: This method has very strange call syntax:
        // ```
        // PollTransiting(^fut, ctx
        // , onReady=fun x ->
        //     doSmthOnReady x
        // , onPending=fun () ->
        //     doSmthOnPending ()
        // )
        // ```
        // but it's library only helper, so it's ok
        static member inline PollTransiting(fut: _ byref, ctx, [<InlineIfLambda>] onReady, [<InlineIfLambda>] onPending) =
            let mutable doLoop = true
            let mutable result = Unchecked.defaultof<'b>
            while doLoop do
                let p = poll ctx fut
                match p with
                | Poll.Ready x ->
                    result <- onReady x
                    doLoop <- false
                | Poll.Pending ->
                    result <- onPending ()
                    doLoop <- false
                | Poll.Transit f ->
                    fut <- f
            result

    [<AutoOpen>]
    module Helpers =
        let inline pollTransiting
            (fut: Future<'a>) (ctx: IContext)
            ([<InlineIfLambda>] onReady: 'a -> 'b)
            ([<InlineIfLambda>] onPending: unit -> 'b)
            ([<InlineIfLambda>] onTransitAction: Future<'a> -> unit)
            : 'b =
            let rec pollTransiting fut =
                let p = poll ctx fut
                match p with
                | Poll.Ready x -> onReady x
                | Poll.Pending -> onPending ()
                | Poll.Transit f ->
                    onTransitAction f
                    pollTransiting f
            pollTransiting fut

    /// <summary> Create the Computation with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Computation returned <code>Ready value</code> when polled </returns>
    let ready (value: 'a) : Future<'a> =
        { new Future<'a> with
            member _.Poll(_ctx) = Poll.Ready value
            member _.Cancel() = () }

    /// <summary> Create the Computation returned <code>Ready ()</code> when polled</summary>
    /// <returns> Computation returned <code>Ready ()value)</code> when polled </returns>
    let readyUnit: Future<unit> =
        { new Future<unit> with
            member _.Poll(_ctx) = Poll.Ready ()
            member _.Cancel() = () }

    /// <summary> Creates always pending Computation </summary>
    /// <returns> always pending Computation </returns>
    let never<'a> : Future<'a> =
        { new Future<'a> with
            member _.Poll(_ctx) = Poll.Pending
            member _.Cancel() = () }

    /// <summary> Creates the Computation lazy evaluator for the passed function </summary>
    /// <returns> Computation lazy evaluator for the passed function </returns>
    let lazy' (f: unit -> 'a) : Future<'a> =
        { new Future<'a> with
            member _.Poll(_ctx) =
                let x = f ()
                Poll.Ready x
            member _.Cancel() = () }

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
    let merge (comp1: Future<'a>) (comp2: Future<'b>) : Future<'a * 'b> =
        upcast MergeFuture(comp1, comp2)

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
    let join (comp: Future<Future<'a>>) : Future<'a> =
        upcast JoinFuture(comp)

    /// <summary> Create a Computation delaying invocation and computation of the Computation of the passed creator </summary>
    /// <returns> Computation delaying invocation and computation of the Computation of the passed creator </returns>
    let delay (creator: unit -> Future<'a>) : Future<'a> =
        { new Future<'a> with
            member _.Poll(_ctx) =
                let fut = creator ()
                Poll.Transit fut
            member _.Cancel() = ( )}

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


    // /// <summary> Creates a IAsyncComputation that raise exception on poll after cancel. Useful for debug. </summary>
    // /// <returns> Fused IAsyncComputation </returns>
    // let inline cancellationFuse (source: Future<'a>) : Future<'a> =
    //     let mutable isCancelled = false
    //     { new Future<'a> with
    //         member _.Poll(ctx) =
    //             if not isCancelled then poll ctx source else raise FutureCancelledException
    //         member _.Cancel() =
    //             isCancelled <- true }

    //#endregion

    //#region STD integration

    let catch (source: Future<'a>) : Future<Result<'a, exn>> =
        let mutable _source = source // TODO: Make separate class for remove FSharpRef in closure
        { new Future<_> with
            member _.Poll(ctx) =
                try
                    pollTransiting _source ctx
                    <| fun x ->
                        Poll.Ready (Ok x)
                    <| fun () -> Poll.Pending
                    <| fun f -> _source <- f
                with e ->
                    Poll.Ready (Error e)
            member _.Cancel() =
                cancelIfNotNull _source
        }

    [<RequireQualifiedAccess>]
    module Seq =

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

    //#endregion

    type SleepFuture(duration: TimeSpan) =
        let mutable _timer: Timer = Unchecked.defaultof<_>
        let mutable _timeOut = false
        interface Future<unit> with
            member _.Poll(ctx) =
                let inline onWake (context: IContext) _ =
                    let timer' = _timer
                    _timer <- Unchecked.defaultof<_>
                    _timeOut <- true
                    context.Wake()
                    timer'.Dispose()
                let inline createTimer context =
                    new Timer(onWake context, null, duration, Timeout.InfiniteTimeSpan)

                if _timeOut then Poll.Ready ()
                else
                    _timer <- createTimer ctx
                    Poll.Pending

            member _.Cancel() =
                _timer.Dispose()

    //#region OS
    let sleep (duration: TimeSpan) : Future<unit> =
        upcast SleepFuture(duration)

    let sleepMs (millisecondDuration: int) =
        let duration = TimeSpan(days=0, hours=0, minutes=0, seconds=0, milliseconds=millisecondDuration)
        sleep duration

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (comp: Future<'a>) : 'a =
        // The simplest implementation of the Future scheduler.
        // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling -> ...)
        // until the point with the result is reached
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let mutable currentFut = comp
        let ctx =
            { new IContext with
                member _.Wake() = wh.Set() |> ignore
                member _.Scheduler = None
            }

        let rec pollWhilePending () =
            let rec pollTransiting () =
                match (poll ctx currentFut) with
                | Poll.Ready x -> x
                | Poll.Pending ->
                    wh.WaitOne() |> ignore
                    pollWhilePending ()
                | Poll.Transit f ->
                    currentFut <- f
                    pollTransiting ()
            pollTransiting ()

        pollWhilePending ()
    //#endregion

    //#region Core ignore
    /// <summary> Creates a Computation that ignore result of the passed Computation </summary>
    /// <returns> Computation that ignore result of the passed Computation </returns>
    let ignore (fut: Future<'a>) : Future<unit> =
        fut |> map ignore

    //#endregion


module Utils =

    // Ограничение на тип структуры для более оптимального использования
    type StructOption<'a when 'a : struct> = Option<'a>

    type Box<'a when 'a : struct> =
        val Inner : 'a
        new(inner: 'a) = { Inner = inner }

    //---------------
    // IntrusiveList

    [<AllowNullLiteral>]
    type IIntrusiveNode<'a> when 'a :> IIntrusiveNode<'a> =
        abstract Next: 'a with get, set

    /// Односвязный список, элементы которого являются его же узлами.
    /// Может быть полезен для исключения дополнительных аллокаций услов на бодобии услов LinkedList.
    /// Например, список ожидающих Context или ожидающих значение 'w: IAsyncComputation
    [<Struct>]
    type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
        val mutable internal startNode: 'a
        val mutable internal endNode: 'a
        new(init: 'a) = { startNode = init; endNode = init }

    module IntrusiveList =
        let create () = IntrusiveList(Unchecked.defaultof<'a>)
        let single x = IntrusiveList(x)

        let isEmpty (list: IntrusiveList<'a>) =
            list.startNode = null || list.endNode = null

        let pushBack (x: 'a) (list: byref<IntrusiveList<'a>>) =
            if isEmpty list then
                list.startNode <- x
                list.endNode <- x
                x.Next <- null
            else
                list.endNode.Next <- x
                list.endNode <- x

        let popFront (list: byref<IntrusiveList<'a>>) =
            if isEmpty list
            then null
            elif list.endNode = list.startNode then
                let r = list.startNode
                list.startNode <- null
                list.endNode <- null
                r
            else
                let first = list.startNode
                let second = list.startNode.Next
                list.startNode <- second
                first

        let toList (list: byref<IntrusiveList<'a>>) : 'a list =
            let root = list.startNode
            let rec collect (c: 'a list) (node: 'a) =
                if node = null then c
                else collect (c @ [node]) node.Next
            collect [] root

    // IntrusiveList
    //---------------
    // OnceVar

    exception OnceVarDoubleWriteException

    // TODO: Optimize to struct without DU
    [<Struct>]
    type private OnceState<'a> =
        // Cancel options only make sense for using OnceVar as IAsyncComputation
        // Re-polling after cancellation is UB by standard,
        // so it is possible to get rid of the cancellation handling in the future.
        | Empty // --> Waiting, HasValue, Cancelled
        | Waiting of ctx: IContext // --> HasValue, Cancelled
        | HasValue of value: 'a // exn on write; --> CancelledWithValue
        | Cancelled // exn on poll; --> CancelledWithValue
        | CancelledWithValue of cancelledValue: 'a // exn on poll; exn on write; STABLE

    /// Low-level immutable cell to asynchronously wait for a put a single value.
    /// Represents the pending computation in which value can be put.
    /// If you never put a value, you will endlessly wait for it.
    [<Class; Sealed>]
    type OnceVar<'a>() =
        let sLock: SpinLock = SpinLock()
        let mutable state = Empty

        /// <returns> false on double write </returns>
        member this.TryWrite(x: 'a) =
            // has state mutation
            let mutable lockTaken = false
            try
                sLock.Enter(&lockTaken)
                match state with
                | Empty ->
                    state <- HasValue x
                    true
                | Waiting context ->
                    state <- HasValue x
                    // exit from lock and wake waiter
                    if lockTaken then lockTaken <- false; sLock.Exit()
                    context.Wake()
                    true
                | Cancelled ->
                    state <- CancelledWithValue x
                    true
                | HasValue _ | CancelledWithValue _ ->
                    false
            finally
                if lockTaken then sLock.Exit()

        member this.Write(x: 'a) =
            if not (this.TryWrite(x)) then raise OnceVarDoubleWriteException

        member this.TryRead() =
            // has NOT state mutation
            let mutable lockTaken = false
            try
                sLock.Enter(&lockTaken)
                match state with
                | HasValue value | CancelledWithValue value -> ValueSome value
                | Empty | Waiting _ | Cancelled -> ValueNone
            finally
                if lockTaken then sLock.Exit()

        member _.TryPoll(context) =
            // has state mutation
            let mutable lockTaken = false
            try
                sLock.Enter(&lockTaken)
                match state with
                | Empty | Waiting _ ->
                    state <- Waiting context
                    Ok Poll.Pending
                | HasValue value ->
                    Ok (Poll.Ready value)
                | Cancelled | CancelledWithValue _ ->
                    Error FutureCancelledException
            finally
                if lockTaken then sLock.Exit()

        interface Future<'a> with
            [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
            member x.Poll(context) =
                let r = x.TryPoll(context)
                match r with
                | Ok x -> x
                | Error ex -> raise ex

            member _.Cancel() =
                // has state mutation
                let mutable lockTaken = false
                try
                    sLock.Enter(&lockTaken)
                    match state with
                    | Empty | Waiting _ -> state <- Cancelled
                    | HasValue x -> state <- CancelledWithValue x
                    | Cancelled | CancelledWithValue _ -> ()
                finally
                    if lockTaken then sLock.Exit()

    module OnceVar =
        /// Create empty IVar instance
        let create () = OnceVar()

        /// Put a value and if it is already set raise exception
        let write (x: 'a) (ovar: OnceVar<'a>) = ovar.Write(x)

        /// Tries to put a value and if it is already set returns an false
        let tryWrite (x: 'a) (ovar: OnceVar<'a>) = ovar.TryWrite(x)

        /// <summary> Returns the future pending value. </summary>
        /// <remarks> IVar itself is a future, therefore
        /// it is impossible to expect or launch this future in two places at once. </remarks>
        let read (ovar: OnceVar<'a>) = ovar :> Future<'a>

        /// Immediately gets the current IVar value and returns Some x if set
        let tryRead (ovar: OnceVar<_>) = ovar.TryRead()

    // --------------------------------------
    // OnceVar END
    // --------------------------------------

    module Experimental =
        [<Struct; StructuralEquality; StructuralComparison>]
        type ObjectOption<'a when 'a : not struct> =
            val Value: 'a
            new(value) = { Value = value }

            member inline this.IsNull =
                obj.ReferenceEquals(this.Value, null)

            member inline this.IsNotNull =
                not this.IsNull

        let inline (|ObjectNone|ObjectSome|) (x: ObjectOption<'a>) =
            if x.IsNull then ObjectNone else ObjectSome x.Value


