[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Channel

open FSharp.Control.Futures


let inline toPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> ISeqStream<'a>
