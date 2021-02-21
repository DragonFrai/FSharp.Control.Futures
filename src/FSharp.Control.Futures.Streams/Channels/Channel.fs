[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Channel

open FSharp.Control.Futures.Streams


let inline asPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> IPullStream<'a>
