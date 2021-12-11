namespace FSharp.Control.Futures.Streams.Channels

open System

open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Streams
open FSharp.Control.Futures.Streams.Core


module Watch =

    [<Sealed>]
    type WatchChannel<'a>() =
        let syncObj = obj()
        let mutable waiter: IContext = nullObj
        let mutable value: ValueOption<'a> = ValueNone
        let mutable isSinkClosed = false
        let mutable isStreamClosed = false

        member _.SyncObj = syncObj
        member val Waiter = waiter with get, set
        member val Value = value with get, set
        member val IsSinkClosed = isSinkClosed with get, set
        member val IsStreamClosed = isStreamClosed with get, set

        interface ISink<'a> with
            member this.Add(x) =
                let put () =
                    if isSinkClosed then raise SinkClosedException
                    if isStreamClosed then
                        false
                    else
                        value <- ValueSome x
                        if isNotNull waiter then waiter.Wake()
                        true
                lock syncObj put

            member this.Close() =
                lock syncObj (fun () ->
                    if isSinkClosed then raise SinkClosedException
                    else isSinkClosed <- true)

        interface IStream<'a> with
            member this.Close() =
                lock syncObj (fun () ->
                    if isStreamClosed then raise StreamClosedException
                    isStreamClosed <- true
                    value <- ValueNone
                    waiter <- Unchecked.defaultof<_>)

            member this.PollNext(cx) =
                lock syncObj (fun () ->
                    if isStreamClosed then raise StreamClosedException
                    if isSinkClosed then StreamPoll.Completed
                    else
                        match value with
                        | ValueSome x -> StreamPoll.Next x
                        | ValueNone ->
                            waiter <- cx
                            StreamPoll.Pending )

    let create (): ISink<'a> * Stream<'a> =
        let ch = WatchChannel()
        upcast ch, upcast ch
