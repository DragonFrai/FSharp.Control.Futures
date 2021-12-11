[<AutoOpen>]
module FSharp.Control.Futures.Streams.FutureBuilderExtension

open FSharp.Control.Futures.Core
open FSharp.Control.Futures


type FutureBuilder with
    member _.For(source: Stream<'a>, action: 'a -> Future<unit>): Future<unit> =
        Stream.iterAsync action source
