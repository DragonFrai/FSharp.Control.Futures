namespace FSharp.Control.Futures

open System.Collections.Generic
open System.Threading
open FSharp.Control.Futures


exception IVarDoublePutException


[<AutoOpen>]
module OnceVarImpl =
    //   /-> Waiting w >-\
    // Empty ---->---- HasValue x / Cancelled
    [<Struct>]
    type private OnceState<'a> =
        | Empty
        | Waiting of ctx: Context
        | HasValue of value: 'a
        | Cancelled
        | CancelledWithValue

    /// An immutable cell to asynchronously wait for a single value.
    /// Represents the pending computation in which value can be put.
    /// If you never put a value, you will endlessly wait for it.
    [<Class; Sealed>]
    type OnceVar<'a>() =
        let syncObj = obj()
        let mutable state = Empty

        // return Error if put of the value failed (IVarDoublePutException)
        member inline private _.TryPutInner(x: 'a) : Result<unit, exn> =
            lock syncObj <| fun () ->
                match state with
                | Empty ->
                    state <- HasValue x
                    Ok ()
                | Waiting context ->
                    state <- HasValue x
                    context.Wake()
                    Ok ()
                | Cancelled ->
                    state <- CancelledWithValue
                    Ok ()
                | HasValue _ | CancelledWithValue ->
                    Error IVarDoublePutException

        member this.TryPut(x: 'a) =
            this.TryPutInner(x)

        member this.Put(x: 'a) =
            match this.TryPutInner(x) with
            | Ok () -> ()
            | Error ex -> raise ex

        member this.TryRead() =
            lock syncObj <| fun () ->
                match state with
                | HasValue value -> ValueSome value
                | Empty | Waiting _ | Cancelled | CancelledWithValue -> ValueNone

        interface IComputation<'a> with

            member _.Poll(context) =
                lock syncObj <| fun () ->
                    match state with
                    | Empty | Waiting _ ->
                        state <- Waiting context
                        Poll.Pending
                    | HasValue value ->
                        Poll.Ready value
                    | Cancelled | CancelledWithValue ->
                        raise FutureCancelledException

            member _.Cancel() =
                lock syncObj <| fun () ->
                    match state with
                    | Empty | Waiting _ ->
                        state <- Cancelled
                    | HasValue _ ->
                        state <- CancelledWithValue
                    | Cancelled | CancelledWithValue -> ()

    module OnceVar =
        /// Create empty IVar instance
        let inline create () = OnceVar()

        /// Put a value and if it is already set raise exception
        let inline write (x: 'a) (ovar: OnceVar<'a>) = ovar.Put(x)

        /// Tries to put a value and if it is already set returns an Error
        let inline tryWrite x (ovar: OnceVar<_>) = ovar.TryPut(x)

        /// <summary> Returns the future pending value. </summary>
        /// <remarks> IVar itself is a future, therefore
        /// it is impossible to expect or launch this future in two places at once. </remarks>
        let inline read (ovar: OnceVar<_>) = ovar :> IComputation<_>

        /// Immediately gets the current IVar value and returns Some x if set
        let inline tryRead (ovar: OnceVar<_>) = ovar.TryRead()




//   /-> Waiting w >-\
// Empty ---->---- HasValue x / Cancelled
[<Struct>]
type private State<'a> =
    | Empty
    | Waiting of ctx: Context
    | HasValue of value: 'a
    | Cancelled
    | CancelledWithValue


/// An immutable cell to asynchronously wait for a single value.
/// Represents the pending future in which value can be put.
/// If you never put a value, you will endlessly wait for it.
[<Class; Sealed>]
type IVar<'a>() =
    let syncObj = obj()
    let mutable _value = Poll.Pending
    let mutable _waiters: LinkedList<Context> = Unchecked.defaultof<_>

    let future =
        let inline comp () =
            let mutable _myCtx: LinkedListNode<Context> = Unchecked.defaultof<_>
            Computation.create
            <| fun ctx ->
                lock syncObj <| fun () ->
                    match _value with
                    | Poll.Ready x ->
                        if not (obj.ReferenceEquals(_myCtx, null)) then
                            _waiters.Remove(_myCtx)
                            _myCtx <- Unchecked.defaultof<_>
                        Poll.Ready x
                    | Poll.Pending ->
                        _myCtx <- _waiters.AddLast(ctx)
                        Poll.Pending
            <| fun () ->
                if not (obj.ReferenceEquals(_myCtx, null)) then
                    _waiters.Remove(_myCtx)
                    _myCtx <- Unchecked.defaultof<_>
        Future.create comp

    // return Error if put of the value failed (IVarDoublePutException)
    member inline private _.TryPutInner(x: 'a) : Result<unit, exn> =
        lock syncObj <| fun () ->
            match _value with
            | Poll.Pending ->
                _value <- Poll.Ready x
                if not (obj.ReferenceEquals(_waiters, null)) then
                    for w in _waiters do
                        w.Wake()
                    _waiters <- Unchecked.defaultof<_>
                    Ok ()
                else
                    Ok ()
            | Poll.Ready _ ->
                Error IVarDoublePutException

    member this.TryPut(x: 'a) =
        this.TryPutInner(x)

    member this.Put(x: 'a) =
        match this.TryPutInner(x) with
        | Ok () -> ()
        | Error ex -> raise ex

    member this.TryRead() =
        lock syncObj <| fun () ->
            match _value with
            | Poll.Ready x -> Some x
            | Poll.Pending -> None

    member this.Read() : Future<'a> = future

module IVar =
    /// Create empty IVar instance
    let inline create () = IVar()

    /// Put a value and if it is already set raise exception
    let inline put (x: 'a) (ivar: IVar<'a>) = ivar.Put(x)

    /// Tries to put a value and if it is already set returns an Error
    let inline tryPut x (ivar: IVar<_>) = ivar.TryPut(x)

    /// <summary> Returns the future pending value. </summary>
    /// <remarks> IVar itself is a future, therefore
    /// it is impossible to expect or launch this future in two places at once. </remarks>
    let inline read (ivar: IVar<_>) = ivar.Read()

    /// Immediately gets the current IVar value and returns Some x if set
    let inline tryRead (ivar: IVar<_>) = ivar.TryRead()

