namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


type Notification<'a> =
    | Next of 'a
    | Completed
    | Error of exn

type IPushStreamSink<'a> =
    abstract Push: Notification<'a> -> Future<unit>

type ISubscription =
    abstract Unsubscribe: unit -> unit

type IPushStream<'a> =
    abstract Subscribe: IPushStreamSink<'a> -> Future<ISubscription>


module PushStream =

    open System

    let single (x: 'a) : IPushStream<'a> = unimpl ""

    let interval (period: TimeSpan) : IPushStream<int> = unimpl ""

    let map (mapping: 'a -> 'b) (source: IPushStream<'a>) : IPushStream<'b> = unimpl ""

    let subscribe (onNext: 'a -> Future<unit>) (source: IPushStream<'a>) : Future<ISubscription> = unimpl ""

    ()


module __ =

    open System

    future {
        let source =
            PushStream.interval (TimeSpan.FromSeconds(2.0))
            |> PushStream.map (fun x -> x + 5)

        let onValue x = future {
            printfn $"> {x}"
        }

        let! subscription =
            source
            |> PushStream.subscribe onValue

        printfn "Subscribed"

        subscription.Unsubscribe()
    }
    |> Future.runSync

    ()
