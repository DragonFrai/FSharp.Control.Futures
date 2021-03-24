namespace FSharp.Control.Futures

open FSharp.Control.Futures


exception IVarDoublePutException

//   /-> Waiting w >-\
// Empty ---->---- HasValue x / Cancelled
[<Struct>]
type private State<'a> =
    | Empty
    | Waiting of ctx: Context
    | HasValue of value: 'a
    | Cancelled
    | CancelledWithValue

// TODO: Rewrite to interlocked if more efficient .
/// An immutable cell to asynchronously wait for a single value.
/// Represents the pending future in which value can be put.
/// If you never put a value, you will endlessly wait for it.
[<Class; Sealed>]
type IVar<'a>() =
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

    interface Future<'a> with

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

module IVar =
    /// Create empty IVar instance
    let inline create () = IVar()

    /// Put a value and if it is already set raise exception
    let inline put x (ivar: IVar<_>) = ivar.Put(x)

    /// Tries to put a value and if it is already set returns an Error
    let inline tryPut x (ivar: IVar<_>) = ivar.TryPut(x)

    /// <summary> Returns the future pending value. </summary>
    /// <remarks> IVar itself is a future, therefore
    /// it is impossible to expect or launch this future in two places at once. </remarks>
    let inline read (ivar: IVar<_>) = ivar :> Future<_>

    /// Immediately gets the current IVar value and returns Some x if set
    let inline tryRead (ivar: IVar<_>) = ivar.TryRead()
