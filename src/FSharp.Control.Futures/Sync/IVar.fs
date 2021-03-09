namespace FSharp.Control.Futures.Sync

open FSharp.Control.Futures


//
//   /-> Waiting w >-\
// Empty ---->---- Value x
[<AutoOpen>]
module State =
    [<Literal>]
    let Empty: int = 0

    [<Literal>]
    let Waiting: int = 1

    [<Literal>]
    let Value: int = 2

    [<Literal>]
    let Cancelled: int = 3



exception IVarDoublePutException

// TODO: Rewrite to interlocked
[<Class; Sealed>]
type IVar<'a>() =
    let syncObj = obj()
    let mutable state = Empty
    let mutable context: Context = Unchecked.defaultof<_>
    let mutable value: 'a = Unchecked.defaultof<_>

    member _.Put(x: 'a) =
        lock syncObj ^fun () ->
            match state with
            | Empty ->
                value <- x
                state <- Value
            | Waiting ->
                value <- x
                state <- Value
                context.Wake()
            | Value ->
                raise IVarDoublePutException
            | Cancelled ->
                value <- x
            | _ ->
                invalidOp "Unreachable"

    interface Future<'a> with
        member _.Poll(context') =
            lock syncObj ^fun () ->
                match state with
                | Empty ->
                    context <- context'
                    state <- Waiting
                    Poll.Pending
                | Waiting ->
                    context <- context'
                    state <- Waiting
                    Poll.Pending
                | Value ->
                    Poll.Ready value
                | Cancelled ->
                    raise FutureCancelledException
                | _ ->
                    invalidOp "Unreachable"

        member _.Cancel() =
            lock syncObj ^fun () ->
                state <- Cancelled

module IVar =
    let create () = IVar()
    // TODO: No-unit return
    let put x (ivar: IVar<_>) = ivar.Put(x)
    let read (ivar: IVar<_>) = ivar :> Future<_>
