[<RequireQualifiedAccess>]
module FSharp.Control.Futures.PollStream.Channels.Channel

open FSharp.Control.Futures.PollStream


let inline asPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> IPollStream<'a>
