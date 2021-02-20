[<RequireQualifiedAccess>]
module FSharp.Control.Futures.SeqStream.Channels.Channel

open FSharp.Control.Futures.SeqStream


let inline asPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> IPollStream<'a>
