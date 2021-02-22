[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures


type FutureBuilder with
    member _.For(source: IPullStream<'a>, action: 'a -> Future<unit>): Future<unit> =
        PullStream.iterAsync action source
