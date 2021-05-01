[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Channel

open FSharp.Control.Futures.Streams


let inline asPair (ch: 'ch) : ISender<'a> * IAsyncStreamer<'a> when 'ch :> ISender<'a> and 'ch :> IAsyncStreamer<'a> =
    ch :> ISender<'a>, ch :> IAsyncStreamer<'a>
