[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Channel

open FSharp.Control.Futures.Streams


let inline asPair (ch: 'ch) : ISender<'a> * IStream<'a> when 'ch :> ISender<'a> and 'ch :> IStream<'a> =
    ch :> ISender<'a>, ch :> IStream<'a>
