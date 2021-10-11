namespace FSharp.Control.Futures.Core

open System
open System.Threading

open FSharp.Control.Futures


/// Exception is thrown when re-polling after cancellation (assuming IAsyncComputation is tracking such an invalid call)
exception FutureCancelledException

[<RequireQualifiedAccess>]
module Future =

    //#region Core

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


module Utils =

    open System.Runtime.CompilerServices

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


