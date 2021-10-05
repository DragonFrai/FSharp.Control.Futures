namespace FSharp.Control.Futures.Streams.Channels

open System
open FSharp.Control.Futures.Core
open FSharp.Control.Futures
open FSharp.Control.Futures.Streams


//   ---------<->------------
//   ↕                      ↕
// Empty --> Waiting ---> Value* --> ClosedWithValue
//   ↓         ↓                        ↓
//   ---->-------------->---------->-----------------> Closed
//
// * -- goes into itself

[<Struct>]
type private WatchState<'a> =
    | Empty // -> Value / Waiting / Closed
    /// Value is ready
    | Value of value: 'a // -> Empty / ClosedWithValue
    /// Exists waiter of value
    | Waiting of context: IContext // -> Value / Closed
    /// Only when Disposed
    | ClosedWithValue of cValue: 'a // -> Closed
    | Closed


// ReceiveOnce : unit -> Future<'a>

// TODO: optimize
type WatchChannel<'a>() =

    let mutable state = WatchState.Empty
    let syncLock = obj()

    interface IChannel<'a> with

        member this.Send(x) =
            lock syncLock ^fun () ->
                match state with
                | Empty -> state <- Value x
                | Value _ -> state <- Value x
                | Waiting context ->
                    state <- Value x
                    context.Wake()
                | ClosedWithValue _ | Closed -> raise (ObjectDisposedException "WatchChannel")

        // Dispose now used only for signalize about invalid use and keep out the ever-waiting receivers
        member this.Dispose() =
            lock syncLock ^fun () ->
                match state with
                | Empty -> state <- Closed
                | Value x -> state <- ClosedWithValue x
                | Waiting context ->
                    state <- Closed
                    context.Wake()
                | Closed | ClosedWithValue _ -> invalidOp "Double dispose"

        member this.PollNext(context) =
            lock syncLock ^fun () ->
                match state with
                | Value x ->
                    state <- Empty
                    StreamPoll.Next x
                | Empty ->
                    state <- Waiting context
                    StreamPoll.Pending
                | Waiting _ -> invalidOp "Call wake-up on wake-up "
                | ClosedWithValue x ->
                    state <- Closed
                    StreamPoll.Next x
                | Closed -> StreamPoll.Completed

        member this.Cancel() =
            lock syncLock ^fun () ->
                // TODO: impl
                //state <- Closed
                do ()

[<RequireQualifiedAccess>]
module Watch =

    let create<'a> () = new WatchChannel<'a>() :> IChannel<_>

    let createPair<'a> () = create<'a> () |> Channel.asPair

