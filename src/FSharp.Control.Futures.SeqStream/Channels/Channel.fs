[<RequireQualifiedAccess>]
module FSharp.Control.Futures.SeqStream.Channels.Channel

open FSharp.Control.Futures.SeqStream


let inline toPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> ISeqStream<'a>
