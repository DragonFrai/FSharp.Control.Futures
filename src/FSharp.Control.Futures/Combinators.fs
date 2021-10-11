[<AutoOpen>]
module FSharp.Control.Futures.Combinators

open FSharp.Control.Futures.Internals


[<RequireQualifiedAccess>]
module Future =

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
