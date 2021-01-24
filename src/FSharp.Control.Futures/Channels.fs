module FSharp.Control.Futures.Channels

open System.Collections.Concurrent
open System.Threading.Channels


type ISender<'T> =
    abstract member Send: 'T -> IFuture<unit>

type IReceiver<'T> =
    abstract member Receive: unit -> IFuture<'T>
    // GetSender ?

type IChannel<'T> =
    inherit ISender<'T>
    inherit IReceiver<'T>

[<RequireQualifiedAccess>]
module Channel =
    let receive (receiver: IReceiver<'a>) =
        receiver.Receive()

    let send (msg: 'a) (sender: ISender<'a>) =
        sender.Send(msg)

type UnboundedChannel<'T>() =
    let queue: ConcurrentQueue<'T> = ConcurrentQueue()
    let mutable waker: Waker option = None

    interface IChannel<'T> with
        member this.Send(msg: 'T): IFuture<unit> =
            legacyfuture {
                queue.Enqueue msg
                match waker with
                | Some waker' ->
                    waker <- None
                    waker' ()
                | None -> ()
            }

        member this.Receive(): IFuture<'T> =
            let innerF waker' =
                let x = queue.TryDequeue()
                match x with
                | true, msg -> Ready msg
                | false, _ ->
                    waker <- Some waker'
                    Pending
            Future.create innerF


[<RequireQualifiedAccess>]
module Channels =
    let mpsc () = UnboundedChannel()
