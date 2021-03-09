[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Channel

open FSharp.Control.Futures.Streams


let inline asPair (ch: 'ch) : ISender<'a> * IPullStream<'a> when 'ch :> ISender<'a> and 'ch :> IPullStream<'a> =
    ch :> ISender<'a>, ch :> IPullStream<'a>
