namespace FSharp.Control.Futures.SeqStream.Channels

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.SeqStream


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
    | Waiting of waker: Waker // -> Value / Closed
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
                | Waiting waker -> state <- Value x; waker ()
                | ClosedWithValue _ | Closed -> raise (ObjectDisposedException "WatchChannel")

        member this.PollNext(waker) =
            lock syncLock ^fun () ->
                match state with
                | Value x -> state <- Empty; Next x
                | Empty -> state <- Waiting waker; Pending
                | Waiting _ -> invalidOp "Call wake-up on wake-up "
                | ClosedWithValue x -> state <- Closed; Next x
                | Closed -> Completed

        // Dispose now used only for signalize about invalid use and keep out the ever-waiting receivers
        member this.Dispose() =
            lock syncLock ^fun () ->
                match state with
                | Empty -> state <- Closed
                | Value x -> state <- ClosedWithValue x
                | Waiting waker -> state <- Closed; waker ()
                | Closed | ClosedWithValue _ -> invalidOp "Double dispose"


[<RequireQualifiedAccess>]
module Watch =

    let create<'a> () = new WatchChannel<'a>() :> IChannel<'a>

    let createPair<'a> () = create<'a> () |> Channel.asPair

