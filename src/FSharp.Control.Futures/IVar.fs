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
    let inline create () = IVar()
    let inline put x (ivar: IVar<_>) = ivar.Put(x)
    let inline tryPut x (ivar: IVar<_>) = ivar.TryPut x
    let inline read (ivar: IVar<_>) = ivar :> Future<_>
