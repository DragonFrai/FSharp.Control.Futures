[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Receiver

open FSharp.Control.Futures


let receive (receiver: IReceiver<'a>) =
    receiver.Receive()



let empty<'a> =
    { new IReceiver<'a> with member _.Receive() = Future.ready (Error Closed) }

let ofSeq (s: 'a seq) =
    let en = s.GetEnumerator()
    { new IReceiver<'a> with member _.Receive() = Future.ready ^if en.MoveNext() then Ok en.Current else Error Closed }

let single (x: 'a) =
    let mutable isReceived = false
    { new IReceiver<'a> with member _.Receive() = Future.ready ^if isReceived then (Error Closed) else isReceived <- true; Ok x }

let map (mapper: 'a -> 'b) (rcv: IReceiver<'a>) : IReceiver<'b> =
    { new IReceiver<'b> with member _.Receive() = Future.map (Result.map mapper) (rcv.Receive()) }
